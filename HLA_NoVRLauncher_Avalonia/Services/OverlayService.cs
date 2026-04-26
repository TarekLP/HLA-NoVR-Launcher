using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	// ---------------------------------------------------------------------------
	// State machine
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Every state the overlay can be in. Transitions are driven purely by
	/// console.log events and user input — never by timers.
	/// </summary>
	public enum OverlayState
	{
		/// <summary>Window exists but is invisible. Game is running normally.</summary>
		Hidden,

		/// <summary>Game just launched and is showing the main menu.</summary>
		MainMenu,

		/// <summary>A level or save is loading. Overlay stays hidden.</summary>
		Loading,

		/// <summary>Player is in-game. Overlay is hidden.</summary>
		InGame,

		/// <summary>Player pressed ESC. Overlay is visible showing the pause menu.</summary>
		Paused
	}

	// ---------------------------------------------------------------------------
	// Service
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Manages the transparent overlay window that sits on top of the HLA game
	/// window and acts as the in-game menu.
	///
	/// Responsibilities:
	///   - Wait for hlvr.exe to appear, then create and position the overlay
	///   - Keep overlay geometry in sync with the game window (10ms loop)
	///   - Tail console.log and drive the state machine from game events
	///   - Send commands back to the game via main_menu_exec.lua + PAUSE key
	///   - Continuously re-assert topmost so the game can't push us behind it
	/// </summary>
	public sealed class OverlayService : IDisposable
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
		// Private state
		// -----------------------------------------------------------------------

		private readonly LauncherHelperService _helper;

		private Window?  _window;
		private IntPtr   _gameHwnd;
		private string?  _gamePath;

		private OverlayState _state = OverlayState.Hidden;

		private CancellationTokenSource? _cts;
		private Task? _geometryTask;
		private Task? _consoleTask;
		private Task? _topmostTask;

		// -----------------------------------------------------------------------
		// Public events
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

		/// <summary>
		/// True once InitializeAsync has been called. If the user launches hlvr.exe
		/// directly this service is never instantiated, so the flag stays false and
		/// the game correctly shows its "start from launcher" message.
		/// </summary>
		public bool WasLaunchedByUs { get; private set; } = false;

		// -----------------------------------------------------------------------
		// Constructor
		// -----------------------------------------------------------------------

		public OverlayService(LauncherHelperService helper)
		{
			_helper = helper ?? throw new ArgumentNullException(nameof(helper));
		}

		// -----------------------------------------------------------------------
		// Initialisation
		// -----------------------------------------------------------------------

		/// <summary>
		/// Waits for hlvr.exe to appear, then creates and positions the overlay
		/// window. Also starts the geometry sync loop, the console monitor, and
		/// the topmost reassertion loop.
		/// </summary>
		public async Task InitializeAsync(
			string gamePath,
			Func<Window> windowFactory,
			CancellationToken cancellationToken = default)
		{
			WasLaunchedByUs = true;
			_gamePath = gamePath;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			Console.WriteLine("[Overlay] Waiting for hlvr.exe...");
			_gameHwnd = await _helper.WaitForGameWindowAsync(
				"hlvr.exe",
				cancellationToken: _cts.Token);
			Console.WriteLine($"[Overlay] Found game hwnd: 0x{_gameHwnd:X}");

			// Check geometry immediately
			var (gx, gy, gw, gh) = _helper.GetWindowGeometry(_gameHwnd);
			Console.WriteLine($"[Overlay] Game geometry: {gx},{gy} {gw}x{gh}");

			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				_window = windowFactory();
				if (gw > 0 && gh > 0)
				{
					var scale = _window.RenderScaling;
					_window.Position = new Avalonia.PixelPoint(gx, gy);
					_window.Width = gw / scale;
					_window.Height = gh / scale;
				}
				else
				{
					Console.WriteLine("[Overlay] WARNING: geometry was zero, using fallback 1280x720");
					_window.Width = 1280;
					_window.Height = 720;
				}

				_window.Show();
				Console.WriteLine("[Overlay] Window shown.");
			});

			await Task.Delay(200, _cts.Token);

			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				IntPtr hwnd = _window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
				Console.WriteLine($"[Overlay] Native hwnd: 0x{hwnd:X}");
				if (hwnd != IntPtr.Zero)
					_helper.SetTopmost(hwnd);
			});

			_geometryTask = RunGeometryLoopAsync(_cts.Token);
			_consoleTask = RunConsoleMonitorAsync(_cts.Token);
			_topmostTask = RunTopmostLoopAsync(_cts.Token);
		}

		// -----------------------------------------------------------------------
		// Game command sending
		// -----------------------------------------------------------------------

		/// <summary>
		/// Sends a console command to the running game.
		/// Flow: write Lua to main_menu_exec.lua → send PAUSE key → game executes.
		/// </summary>
		public async Task SendCommandAsync(string consoleCommand)
		{
			if (string.IsNullOrEmpty(_gamePath))
				throw new InvalidOperationException("OverlayService is not initialised.");

			string lua  = $"SendToConsole(\"{EscapeLua(consoleCommand)}\")";
			string path = Path.Combine(_gamePath, LuaExecRelPath);

			await File.WriteAllTextAsync(path, lua);
			_helper.ExecuteCommand("exec", "HLA-NoVR-Launcher-Helper.exe");
		}

		// -----------------------------------------------------------------------
		// Visibility helpers
		// -----------------------------------------------------------------------

		public void Show() => Dispatcher.UIThread.Post(() => _window?.Show());
		public void Hide() => Dispatcher.UIThread.Post(() => _window?.Hide());

		// -----------------------------------------------------------------------
		// Dispose
		// -----------------------------------------------------------------------

		public void Dispose()
		{
			_cts?.Cancel();

			try { _geometryTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
			try { _consoleTask?.Wait(TimeSpan.FromSeconds(2));  } catch { }
			try { _topmostTask?.Wait(TimeSpan.FromSeconds(2));  } catch { }

			_cts?.Dispose();

			Dispatcher.UIThread.Post(() => _window?.Close());
		}

		// -----------------------------------------------------------------------
		// State machine
		// -----------------------------------------------------------------------

		private void TransitionTo(OverlayState next)
		{
			if (_state == next) return;

			_state = next;
			StateChanged?.Invoke(next);

			switch (next)
			{
				case OverlayState.MainMenu:
				case OverlayState.Paused:
					Show();
					break;

				case OverlayState.Hidden:
				case OverlayState.Loading:
				case OverlayState.InGame:
					Hide();
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
					AchievementReceived?.Invoke(parts[1].Trim());
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
						Dispatcher.UIThread.Post(() =>
						{
							if (_window == null) return;

							// GetWindowGeometry returns physical pixels.
							// Avalonia Width/Height are logical pixels.
							// Divide by RenderScaling to convert correctly.
							// (e.g. at 150% DPI: 2560px / 1.5 = 1706 logical px)
							var scale = _window.RenderScaling;

							_window.Position = new Avalonia.PixelPoint(x, y);
							_window.Width    = w / scale;
							_window.Height   = h / scale;

							// Re-assert topmost on every geometry change.
							// The game can knock our window behind it when it
							// resizes or receives focus events.
							IntPtr hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
							if (hwnd != IntPtr.Zero)
								_helper.SetTopmost(hwnd);
						});
					},
					ct);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[OverlayService] Geometry loop error: {ex.Message}");
				GameExited?.Invoke();
			}
		}

		// -----------------------------------------------------------------------
		// Topmost reassertion loop
		// -----------------------------------------------------------------------

		/// <summary>
		/// Re-asserts topmost every 500ms as a backstop. The game window can
		/// reclaim topmost during its own initialization or when regaining focus
		/// and the geometry loop only fires on window moves/resizes — so we need
		/// this separate heartbeat to cover static fullscreen-windowed scenarios.
		/// </summary>
		private async Task RunTopmostLoopAsync(CancellationToken ct)
		{
			try
			{
				while (!ct.IsCancellationRequested)
				{
					await Task.Delay(500, ct);

					if (_window == null) continue;

					await Dispatcher.UIThread.InvokeAsync(() =>
					{
						IntPtr hwnd = _window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
						if (hwnd != IntPtr.Zero)
							_helper.SetTopmost(hwnd);
					});
				}
			}
			catch (OperationCanceledException) { }
		}

		// -----------------------------------------------------------------------
		// Console monitor (positional tail)
		// -----------------------------------------------------------------------

		/// <summary>
		/// Tails console.log from the end of the file at startup, so old messages
		/// from previous sessions are ignored. Only lines written after the launcher
		/// started are processed.
		/// </summary>
		private async Task RunConsoleMonitorAsync(CancellationToken ct)
		{
			string consolePath = Path.Combine(_gamePath!, ConsoleLogRelPath);

			// Start at the current end of the file — ignore everything written
			// by previous game sessions. Without this, stale "[GameMenu]" lines
			// trigger false state transitions on every launch.
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
							// Must share with hlvr.exe which keeps the file open for writing
							FileShare.ReadWrite);

						// Seek to where we left off; reset if file was rotated/truncated
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
					catch (IOException)
					{
						// File briefly locked by the game — skip this poll
					}

					await Task.Delay(100, ct);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[OverlayService] Console monitor error: {ex.Message}");
			}
		}

		// -----------------------------------------------------------------------
		// Helpers
		// -----------------------------------------------------------------------

		/// <summary>
		/// Reads the current game window geometry and applies it to the overlay
		/// immediately. Must be called on the UI thread.
		/// </summary>
		private void SyncGeometry()
		{
			if (_window == null || _gameHwnd == IntPtr.Zero) return;

			var (x, y, w, h) = _helper.GetWindowGeometry(_gameHwnd);
			var scale = _window.RenderScaling;

			_window.Position = new Avalonia.PixelPoint(x, y);
			_window.Width    = w / scale;
			_window.Height   = h / scale;
		}

		/// <summary>Escapes a string for safe embedding inside a Lua string literal.</summary>
		private static string EscapeLua(string s) =>
			s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
	}
}
