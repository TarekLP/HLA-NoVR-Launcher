using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	// ---------------------------------------------------------------------------
	// Service
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Drop-in replacement for <see cref="OverlayService"/> that uses a Godot
	/// process as the overlay instead of an Avalonia window.
	///
	/// Responsibilities:
	///   - Wait for hlvr.exe to appear
	///   - Launch the Godot overlay process (transparent, always-on-top)
	///   - Position the Godot window over the game using Win32 (same as before)
	///   - Keep the Godot window geometry in sync with the game window
	///   - Tail console.log and drive the state machine from game events
	///   - Push state changes and commands to Godot via <see cref="GodotBridge"/>
	///   - Send console commands back to the game via Lua + PAUSE key
	///
	/// What changed vs OverlayService:
	///   - No Avalonia Window — all UI lives in the Godot process
	///   - Show/Hide are IPC commands instead of Window.Show()/Hide()
	///   - Geometry sync moves the Godot HWND via Win32 instead of Avalonia layout
	///   - A GodotBridge pipe is started before launching Godot
	/// </summary>
	public sealed class GodotOverlayService : IAsyncDisposable
	{
		// -----------------------------------------------------------------------
		// Constants
		// -----------------------------------------------------------------------

		private const string LuaExecRelPath   = "game/hlvr/scripts/vscripts/main_menu_exec.lua";
		private const string ConsoleLogRelPath = "game/hlvr/console.log";

		private const string LineMainMenu    = "[GameMenu] main_menu_mode";
		private const string LinePause       = "[GameMenu] hide";
		private const string LineAchievement = "[GameMenu] give_achievement";
		private const string LineLoading     = "CHostStateMgr::QueueNewRequest( Loading";
		private const string LineRestoring   = "CHostStateMgr::QueueNewRequest( Restoring Save";

		// -----------------------------------------------------------------------
		// Dependencies
		// -----------------------------------------------------------------------

		private readonly LauncherHelperService _helper;
		private readonly GodotBridge           _bridge;

		// -----------------------------------------------------------------------
		// State
		// -----------------------------------------------------------------------

		private IntPtr  _gameHwnd;
		private IntPtr  _godotHwnd;
		private string? _gamePath;

		private Process?  _godotProcess;
		private OverlayState _state = OverlayState.Hidden;

		private CancellationTokenSource? _cts;
		private Task? _geometryTask;
		private Task? _consoleTask;
		private Task? _topmostTask;

		// -----------------------------------------------------------------------
		// Public events  (same contract as OverlayService)
		// -----------------------------------------------------------------------

		/// <summary>Fired every time the overlay state changes.</summary>
		public event Action<OverlayState>? StateChanged;

		/// <summary>Fired when the game sends a give_achievement command.</summary>
		public event Action<string>? AchievementReceived;

		/// <summary>Fired when the game window disappears (game closed/crashed).</summary>
		public event Action? GameExited;

		// -----------------------------------------------------------------------
		// Public properties
		// -----------------------------------------------------------------------

		public OverlayState State => _state;

		/// <inheritdoc cref="OverlayService.WasLaunchedByUs"/>
		public bool WasLaunchedByUs { get; private set; } = false;

		// -----------------------------------------------------------------------
		// Constructor
		// -----------------------------------------------------------------------

		/// <param name="helper">Shared Win32 / process helper.</param>
		/// <param name="bridge">
		///   Optional: supply your own GodotBridge if you want to control the pipe
		///   name or reuse it elsewhere. If null, a default bridge is created.
		/// </param>
		public GodotOverlayService(
			LauncherHelperService helper,
			GodotBridge?          bridge = null)
		{
			_helper = helper ?? throw new ArgumentNullException(nameof(helper));
			_bridge = bridge ?? new GodotBridge();
		}

		// -----------------------------------------------------------------------
		// Initialisation
		// -----------------------------------------------------------------------

		/// <summary>
		/// Waits for hlvr.exe, launches the Godot overlay, positions it over the
		/// game, then starts the geometry/console/topmost loops.
		/// </summary>
		/// <param name="gamePath">Absolute path to the HLA game folder.</param>
		/// <param name="godotExePath">Absolute path to the Godot overlay .exe.</param>
		/// <param name="cancellationToken">Optional external cancellation.</param>
		public async Task InitializeAsync(
			string            gamePath,
			string            godotExePath,
			CancellationToken cancellationToken = default)
		{
			WasLaunchedByUs = true;
			_gamePath        = gamePath;
			_cts             = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			// Step 1: Start the pipe server BEFORE launching Godot so the server
			// is ready when Godot tries to connect on startup.
			await _bridge.StartAsync(_cts.Token);
			_bridge.ClientConnected    += OnGodotConnected;
			_bridge.ClientDisconnected += OnGodotDisconnected;

			// Step 2: Wait for the game so we can position the overlay correctly.
			Console.WriteLine("[GodotOverlay] Waiting for hlvr.exe...");
			_gameHwnd = await _helper.WaitForGameWindowAsync(
				"hlvr.exe",
				cancellationToken: _cts.Token);
			Console.WriteLine($"[GodotOverlay] Found game hwnd: 0x{_gameHwnd:X}");

			var (gx, gy, gw, gh) = _helper.GetWindowGeometry(_gameHwnd);
			Console.WriteLine($"[GodotOverlay] Game geometry: {gx},{gy} {gw}x{gh}");

			// Step 3: Launch the Godot overlay process.
			// We pass:
			//   --pipe-name     so Godot knows which pipe to connect to
			//   --position      initial x,y so the window appears in the right place immediately
			//   --size          initial w,h
			// Godot reads these from OS.get_cmdline_args() on startup.
			_godotProcess = LaunchGodotProcess(godotExePath, gx, gy, gw, gh);
			Console.WriteLine($"[GodotOverlay] Godot process started (PID {_godotProcess.Id}).");

			// Step 4: Find the Godot window handle so we can position it via Win32.
			// Give Godot a moment to create its native window before we look for it.
			await Task.Delay(500, _cts.Token);
			_godotHwnd = await _helper.WaitForGameWindowAsync(
				Path.GetFileNameWithoutExtension(godotExePath),
				cancellationToken: _cts.Token);
			Console.WriteLine($"[GodotOverlay] Found Godot hwnd: 0x{_godotHwnd:X}");

			// Step 5: Wire up Win32 ownership — same as the old OverlayService.
			// SetParent → NoActivate → Topmost, in that order.
			if (_godotHwnd != IntPtr.Zero)
			{
				_helper.SetParent(_godotHwnd, _gameHwnd);
				_helper.SetNoActivate(_godotHwnd);
				_helper.SetTopmost(_godotHwnd);

				// Apply the initial bounds immediately via Win32.
				// (Godot may not have connected to the pipe yet.)
				if (gw > 0 && gh > 0)
					_helper.SetWindowBounds(_godotHwnd, gx, gy, gw, gh);
			}
			else
			{
				Console.WriteLine("[GodotOverlay] WARNING: Godot hwnd not found.");
			}

			_geometryTask = RunGeometryLoopAsync(_cts.Token);
			_consoleTask  = RunConsoleMonitorAsync(_cts.Token);
			_topmostTask  = RunTopmostLoopAsync(_cts.Token);
		}

		// -----------------------------------------------------------------------
		// Game command sending  (unchanged from OverlayService)
		// -----------------------------------------------------------------------

		/// <summary>
		/// Sends a console command to the running game.
		/// Flow: write Lua to main_menu_exec.lua → send PAUSE key → game executes.
		/// </summary>
		public async Task SendCommandAsync(string consoleCommand)
		{
			if (string.IsNullOrEmpty(_gamePath))
				throw new InvalidOperationException("GodotOverlayService is not initialised.");

			string lua  = $"SendToConsole(\"{EscapeLua(consoleCommand)}\")";
			string path = Path.Combine(_gamePath, LuaExecRelPath);

			await File.WriteAllTextAsync(path, lua);
			_helper.ExecuteCommand("exec", "HLA-NoVR-Launcher-Helper.exe");
		}

		// -----------------------------------------------------------------------
		// Show / Hide  (now IPC commands instead of Window.Show/Hide)
		// -----------------------------------------------------------------------

		public async Task ShowAsync() => await _bridge.SendAsync(OverlayCommand.Show());
		public async Task HideAsync() => await _bridge.SendAsync(OverlayCommand.Hide());

		// -----------------------------------------------------------------------
		// Bridge event handlers
		// -----------------------------------------------------------------------

		private void OnGodotConnected()
		{
			Console.WriteLine("[GodotOverlay] Godot connected to pipe. Syncing state.");

			// Push the current state immediately so Godot renders the right panel
			// without waiting for the next console.log event.
			_ = _bridge.SendAsync(OverlayCommand.SetState(_state));

			if (_godotHwnd != IntPtr.Zero)
			{
				var (x, y, w, h) = _helper.GetWindowGeometry(_gameHwnd);
				_ = _bridge.SendAsync(OverlayCommand.SetBounds(x, y, w, h));
			}
		}

		private void OnGodotDisconnected()
		{
			Console.WriteLine("[GodotOverlay] Godot disconnected from pipe.");
		}

		// -----------------------------------------------------------------------
		// Dispose
		// -----------------------------------------------------------------------

		public async ValueTask DisposeAsync()
		{
			// Tell Godot to shut down gracefully before killing the process
			await _bridge.SendAsync(OverlayCommand.Shutdown());
			await Task.Delay(300);

			_cts?.Cancel();

			try { if (_geometryTask != null) await _geometryTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
			try { if (_consoleTask  != null) await _consoleTask .WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
			try { if (_topmostTask  != null) await _topmostTask .WaitAsync(TimeSpan.FromSeconds(2)); } catch { }

			await _bridge.DisposeAsync();

			if (_godotProcess is { HasExited: false })
			{
				try { _godotProcess.Kill(); }
				catch (Exception ex) { Console.WriteLine($"[GodotOverlay] Kill failed: {ex.Message}"); }
			}

			_godotProcess?.Dispose();
			_cts?.Dispose();
		}

		// -----------------------------------------------------------------------
		// State machine  (identical logic to OverlayService)
		// -----------------------------------------------------------------------

		private void TransitionTo(OverlayState next)
		{
			if (_state == next) return;

			_state = next;
			StateChanged?.Invoke(next);

			// Push the new state to Godot so it can switch panels
			_ = _bridge.SendAsync(OverlayCommand.SetState(next));

			// Show/hide the Godot window at the Win32 level as a backstop.
			// Godot's own visibility logic can handle the fine-grained panels,
			// but Win32-level hide ensures no overlay flicker when fully hidden.
			switch (next)
			{
				case OverlayState.MainMenu:
				case OverlayState.Paused:
					if (_godotHwnd != IntPtr.Zero) _helper.ShowWindow(_godotHwnd);
					break;

				case OverlayState.Hidden:
				case OverlayState.Loading:
				case OverlayState.InGame:
					if (_godotHwnd != IntPtr.Zero) _helper.HideWindow(_godotHwnd);
					break;
			}
		}

		private void HandleConsoleLine(string line)
		{
			if (line.Contains(LineMainMenu))
			{
				TransitionTo(OverlayState.MainMenu);
			}
			else if (line.Contains(LinePause))
			{
				TransitionTo(OverlayState.Paused);
			}
			else if (line.Contains(LineLoading) || line.Contains(LineRestoring))
			{
				TransitionTo(OverlayState.Loading);
			}
			else if (line.Contains(LineAchievement))
			{
				string[] parts = line.Split(
					new[] { "[GameMenu] give_achievement " },
					StringSplitOptions.None);

				if (parts.Length > 1)
				{
					string id = parts[1].Trim();
					AchievementReceived?.Invoke(id);
					_ = _bridge.SendAsync(OverlayCommand.ShowAchievement(id));
				}
			}
		}

		// -----------------------------------------------------------------------
		// Geometry sync loop
		// -----------------------------------------------------------------------

		private async Task RunGeometryLoopAsync(CancellationToken ct)
		{
			try
			{
				await _helper.MonitorGameWindowAsync(
					_gameHwnd,
					geometry =>
					{
						var (x, y, w, h) = geometry;

						// Move the Godot window via Win32 (physical pixels, no DPI dance needed)
						if (_godotHwnd != IntPtr.Zero)
							_helper.SetWindowBounds(_godotHwnd, x, y, w, h);

						// Also tell Godot via IPC so it can re-layout its CanvasLayers
						_ = _bridge.SendAsync(OverlayCommand.SetBounds(x, y, w, h));
					},
					ct);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[GodotOverlay] Geometry loop error: {ex.Message}");
				GameExited?.Invoke();
			}
		}

		// -----------------------------------------------------------------------
		// Topmost reassertion loop  (unchanged)
		// -----------------------------------------------------------------------

		private async Task RunTopmostLoopAsync(CancellationToken ct)
		{
			try
			{
				while (!ct.IsCancellationRequested)
				{
					await Task.Delay(500, ct);

					if (_godotHwnd != IntPtr.Zero)
						_helper.SetTopmost(_godotHwnd);
				}
			}
			catch (OperationCanceledException) { }
		}

		// -----------------------------------------------------------------------
		// Console monitor  (identical to OverlayService)
		// -----------------------------------------------------------------------

		private async Task RunConsoleMonitorAsync(CancellationToken ct)
		{
			string consolePath = Path.Combine(_gamePath!, ConsoleLogRelPath);

			long filePos = File.Exists(consolePath)
				? new FileInfo(consolePath).Length
				: 0;

			try
			{
				while (!ct.IsCancellationRequested)
				{
					if (!File.Exists(consolePath))
					{
						await Task.Delay(500, ct);
						continue;
					}

					try
					{
						using var fs = new FileStream(
							consolePath,
							FileMode.Open,
							FileAccess.Read,
							FileShare.ReadWrite);

						fs.Seek(filePos <= fs.Length ? filePos : 0, SeekOrigin.Begin);

						using var reader = new StreamReader(fs);
						string? line;
						while ((line = reader.ReadLine()) != null)
						{
							if (!string.IsNullOrWhiteSpace(line))
								HandleConsoleLine(line);
						}

						filePos = fs.Position;
					}
					catch (IOException) { /* File briefly locked — skip poll */ }

					await Task.Delay(100, ct);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[GodotOverlay] Console monitor error: {ex.Message}");
			}
		}

		// -----------------------------------------------------------------------
		// Godot process launcher
		// -----------------------------------------------------------------------

		private static Process LaunchGodotProcess(string exePath, int x, int y, int w, int h)
		{
			var info = new ProcessStartInfo
			{
				FileName        = exePath,
				UseShellExecute = false,

				// These arguments are read by the Godot overlay on startup.
				// The GDScript bootstrap reads them with OS.get_cmdline_args()
				// to connect on the correct port and set its initial position.
				Arguments = string.Join(" ",
					$"--overlay-port {GodotBridge.DefaultPort}",
					$"--overlay-x {x}",
					$"--overlay-y {y}",
					$"--overlay-w {w}",
					$"--overlay-h {h}")
			};

			return Process.Start(info)
				?? throw new InvalidOperationException($"Failed to start Godot process: {exePath}");
		}

		// -----------------------------------------------------------------------
		// Helpers
		// -----------------------------------------------------------------------

		private static string EscapeLua(string s) =>
			s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
	}
}
