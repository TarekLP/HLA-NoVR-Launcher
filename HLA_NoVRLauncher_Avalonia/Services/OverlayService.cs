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
	///   - Keep overlay geometry in sync with the game window (10 ms loop)
	///   - Tail console.log and drive the state machine from game events
	///   - Send commands back to the game via main_menu_exec.lua + PAUSE key
	/// </summary>
	public sealed class OverlayService : IDisposable
	{
		// -----------------------------------------------------------------------
		// Constants
		// -----------------------------------------------------------------------

		private const string LuaExecRelPath   = "game/hlvr/scripts/vscripts/main_menu_exec.lua";
		private const string ConsoleLogRelPath = "game/hlvr/console.log";

		// Lines the console monitor watches for
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

		// -----------------------------------------------------------------------
		// Public events
		// -----------------------------------------------------------------------

		/// <summary>Fired every time the overlay state changes.</summary>
		public event Action<OverlayState>? StateChanged;

		/// <summary>
		/// Fired when the game sends a give_achievement command.
		/// The string argument is the achievement ID.
		/// </summary>
		public event Action<string>? AchievementReceived;

		/// <summary>Fired when the game window disappears (game closed/crashed).</summary>
		public event Action? GameExited;

		// -----------------------------------------------------------------------
		// Public properties
		// -----------------------------------------------------------------------

		public OverlayState State => _state;

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
		/// window. Also starts the geometry sync loop and the console monitor.
		///
		/// The <paramref name="windowFactory"/> delegate is called on the UI thread
		/// after the game window is found so callers can create their Avalonia
		/// window normally.
		/// </summary>
		public async Task InitializeAsync(
			string            gamePath,
			Func<Window>      windowFactory,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(gamePath))
				throw new ArgumentException("gamePath cannot be empty", nameof(gamePath));

			_gamePath = gamePath;
			_cts      = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			// 1. Wait for game window (blocks until hlvr.exe appears or timeout)
			_gameHwnd = await _helper.WaitForGameWindowAsync(
				"hlvr.exe",
				cancellationToken: _cts.Token);

			// 2. Create the overlay window on the UI thread, position it immediately
			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				_window = windowFactory();
				_window.Show();
				SyncGeometry();
			});

			// 3. Start background loops
			_geometryTask = RunGeometryLoopAsync(_cts.Token);
			_consoleTask  = RunConsoleMonitorAsync(_cts.Token);
		}

		// -----------------------------------------------------------------------
		// Game command sending
		// -----------------------------------------------------------------------

		/// <summary>
		/// Sends a console command to the running game.
		/// Flow: write Lua to main_menu_exec.lua -> send PAUSE key -> game executes.
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



		public void Show() => Dispatcher.UIThread.Post(() => _window?.Show());
		public void Hide() => Dispatcher.UIThread.Post(() => _window?.Hide());



		public void Dispose()
		{
			_cts?.Cancel();

			try { _geometryTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
			try { _consoleTask?.Wait(TimeSpan.FromSeconds(2));  } catch { }

			_cts?.Dispose();

			Dispatcher.UIThread.Post(() => _window?.Close());
		}


		/// <summary>
		/// Transitions to a new state and shows/hides the window accordingly.
		/// All callers are on the console-monitor background thread; Show/Hide
		/// dispatch to the UI thread internally so this is safe to call from anywhere.
		/// </summary>
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

		/// <summary>
		/// Interprets one new line from console.log and drives the state machine.
		/// </summary>
		private void HandleConsoleLine(string line)
		{
			if (line.Contains(LineMainMenu))
			{
				TransitionTo(OverlayState.MainMenu);
			}
			else if (line.Contains(LinePause))
			{
				// "[GameMenu] hide" means the game wants us to show the pause menu
				TransitionTo(OverlayState.Paused);
			}
			else if (line.Contains(LineLoading) || line.Contains(LineRestoring))
			{
				TransitionTo(OverlayState.Loading);
			}
			else if (line.Contains(LineAchievement))
			{
				// "[GameMenu] give_achievement <id>"
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
						// MonitorGameWindowAsync only fires when geometry actually changes,
						// so every invocation here is a real resize or move — apply it.
						var (x, y, w, h) = geometry;
						Dispatcher.UIThread.Post(() =>
						{
							if (_window == null) return;
							_window.Position = new Avalonia.PixelPoint(x, y);
							_window.Width    = w;
							_window.Height   = h;
						});
					},
					ct);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[OverlayService] Geometry loop error: {ex.Message}");
				// Geometry loop dying almost always means the game window is gone
				GameExited?.Invoke();
			}
		}

		// -----------------------------------------------------------------------
		// Console monitor (positional tail)
		// -----------------------------------------------------------------------

		/// <summary>
		/// Tails console.log from the last-read byte position, so each poll only
		/// processes lines the game wrote since the previous poll. This avoids
		/// re-scanning the entire file (which grows continuously) every 100 ms.
		/// </summary>
		private async Task RunConsoleMonitorAsync(CancellationToken ct)
		{
			string consolePath = Path.Combine(_gamePath!, ConsoleLogRelPath);
			long   filePos     = 0;

			try
			{
				while (!ct.IsCancellationRequested)
				{
					// The log file doesn't exist until the game has started up far enough
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

						// Seek to where we left off; reset if the file was rotated/truncated
						fs.Seek(filePos <= fs.Length ? filePos : 0, SeekOrigin.Begin);

						using var reader = new StreamReader(fs);
						string? line;
						while ((line = reader.ReadLine()) != null)
						{
							if (!string.IsNullOrWhiteSpace(line))
								HandleConsoleLine(line);
						}

						// Remember position so next poll starts from here
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
		/// immediately. Call once on startup before the geometry loop begins.
		/// Must be called on the UI thread.
		/// </summary>
		private void SyncGeometry()
		{
			if (_window == null || _gameHwnd == IntPtr.Zero) return;

			var (x, y, w, h) = _helper.GetWindowGeometry(_gameHwnd);
			_window.Position = new Avalonia.PixelPoint(x, y);
			_window.Width    = w;
			_window.Height   = h;
		}

		/// <summary>Escapes a string for safe embedding inside a Lua string literal.</summary>
		private static string EscapeLua(string s) =>
			s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
	}
}
