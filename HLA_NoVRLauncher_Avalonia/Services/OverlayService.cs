using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	/// <summary>
	/// Manages the Avalonia overlay window over the HLA game window.
	/// The window itself is created via a factory so this service has no
	/// dependency on the Views layer (avoiding circular references).
	/// </summary>
	public sealed class OverlayService : IDisposable
	{
		// -----------------------------------------------------------------------
		// Win32
		// -----------------------------------------------------------------------

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SetWindowPos(
			IntPtr hWnd, IntPtr hWndInsertAfter,
			int x, int y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

		private static readonly IntPtr HWND_TOPMOST   = new(-1);
		private const uint SWP_NOMOVE     = 0x0002;
		private const uint SWP_NOSIZE     = 0x0001;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_SHOWWINDOW = 0x0040;
		private const int  GWL_EXSTYLE    = -20;
		private const uint WS_EX_NOACTIVATE  = 0x08000000;
		private const int  GWLP_HWNDPARENT   = -8;

		// -----------------------------------------------------------------------
		// Constants
		// -----------------------------------------------------------------------

		private const string LuaExecRelPath   = "game/hlvr/scripts/vscripts/main_menu_exec.lua";
		private const string ConsoleLogRelPath = "game/hlvr/console.log";

		private const string LineMainMenu  = "[GameMenu] main_menu_mode";
		private const string LinePause     = "[GameMenu] hide";
		private const string LineAchievement = "[GameMenu] give_achievement";
		private const string LineLoading   = "CHostStateMgr::QueueNewRequest( Loading";
		private const string LineRestoring = "CHostStateMgr::QueueNewRequest( Restoring Save";

		// -----------------------------------------------------------------------
		// State
		// -----------------------------------------------------------------------

		private readonly LauncherHelperService _helper;
		private readonly Func<Window> _windowFactory;
		private Window? _window;
		private IntPtr _gameHwnd;
		private IntPtr _overlayHwnd;
		private string? _gamePath;

		private OverlayState _state = OverlayState.Hidden;

		private CancellationTokenSource? _cts;
		private Task? _geometryTask;
		private Task? _consoleTask;
		private Task? _topmostTask;

		private bool _disposed;

		// -----------------------------------------------------------------------
		// Public events
		// -----------------------------------------------------------------------

		public event Action<OverlayState>? StateChanged;
		public event Action<string>?       AchievementReceived;
		public event Action?               GameExited;

		public OverlayState State => _state;

		// -----------------------------------------------------------------------
		// Constructor
		// -----------------------------------------------------------------------

		public OverlayService(LauncherHelperService helper, Func<Window> windowFactory)
		{
			_helper        = helper         ?? throw new ArgumentNullException(nameof(helper));
			_windowFactory = windowFactory  ?? throw new ArgumentNullException(nameof(windowFactory));
		}

		// -----------------------------------------------------------------------
		// Initialise
		// -----------------------------------------------------------------------

		/// <summary>
		/// Waits for the game window, shows the overlay over it, then starts
		/// the geometry sync, topmost reassertion, and console monitor loops.
		/// Must be called from a background thread — Show() is marshalled to UI.
		/// </summary>
		public async Task InitializeAsync(string gamePath, CancellationToken ct = default)
		{
			_gamePath = gamePath;
			_cts      = CancellationTokenSource.CreateLinkedTokenSource(ct);

			// Step 1 — wait for the game window
			Console.WriteLine("[Overlay] Waiting for HLA window...");
			_gameHwnd = await _helper.WaitForGameWindowAsync("hlvr.exe", cancellationToken: _cts.Token);
			Console.WriteLine($"[Overlay] Game hwnd: 0x{_gameHwnd:X}");

			var (gx, gy, gw, gh) = _helper.GetWindowGeometry(_gameHwnd);
			Console.WriteLine($"[Overlay] Game geometry: {gx},{gy} {gw}x{gh}");

			// Step 2 — create and show the Avalonia window on the UI thread
			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				_window = _windowFactory();

				// Size to match game before showing, so there's no flash
				if (gw > 0 && gh > 0)
				{
					_window.Width  = gw / _window.RenderScaling;
					_window.Height = gh / _window.RenderScaling;
				}

				_window.Show();
				_overlayHwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
				Console.WriteLine($"[Overlay] Overlay hwnd: 0x{_overlayHwnd:X}");
			});

			if (_overlayHwnd == IntPtr.Zero)
			{
				Console.WriteLine("[Overlay] ERROR: could not get overlay HWND — aborting.");
				return;
			}

			// Step 3 — Win32 setup: parent, no-activate, topmost
			ApplyWin32Flags(_overlayHwnd, _gameHwnd);

			// Step 4 — apply initial geometry via Win32 (physical pixels, no DPI dance)
			if (gw > 0 && gh > 0)
				SetWindowPos(_overlayHwnd, HWND_TOPMOST, gx, gy, gw, gh, SWP_NOACTIVATE | SWP_SHOWWINDOW);

			// Step 5 — start background loops
			_geometryTask = RunGeometryLoopAsync(_cts.Token);
			_topmostTask  = RunTopmostLoopAsync(_cts.Token);
			_consoleTask  = RunConsoleMonitorAsync(_cts.Token);

			Console.WriteLine("[Overlay] Initialised successfully.");
		}

		// -----------------------------------------------------------------------
		// Win32 helpers
		// -----------------------------------------------------------------------

		private void ApplyWin32Flags(IntPtr overlay, IntPtr game)
		{
			// Make overlay an owned window of the game so it stays above it
			SetWindowLongPtr(overlay, GWLP_HWNDPARENT, game);

			// Prevent overlay from stealing focus when clicked
			IntPtr exStyle = GetWindowLongPtr(overlay, GWL_EXSTYLE);
			SetWindowLongPtr(overlay, GWL_EXSTYLE, (IntPtr)((ulong)exStyle | WS_EX_NOACTIVATE));

			// Force topmost immediately
			SetWindowPos(overlay, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
		}

		// -----------------------------------------------------------------------
		// Background loops
		// -----------------------------------------------------------------------

		private async Task RunGeometryLoopAsync(CancellationToken ct)
		{
			try
			{
				await _helper.MonitorGameWindowAsync(_gameHwnd, geometry =>
				{
					var (x, y, w, h) = geometry;

					// Move overlay via Win32 (physical pixels)
					if (_overlayHwnd != IntPtr.Zero)
						SetWindowPos(_overlayHwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE);

					// Also update Avalonia logical size so layout scales correctly
					if (_window != null && w > 0 && h > 0)
					{
						Dispatcher.UIThread.Post(() =>
						{
							_window.Width  = w / _window.RenderScaling;
							_window.Height = h / _window.RenderScaling;
						});
					}
				}, ct);
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[Overlay] Geometry loop error: {ex.Message}");
				GameExited?.Invoke();
			}
		}

		private async Task RunTopmostLoopAsync(CancellationToken ct)
		{
			try
			{
				while (!ct.IsCancellationRequested)
				{
					await Task.Delay(500, ct);
					if (_overlayHwnd != IntPtr.Zero)
						SetWindowPos(_overlayHwnd, HWND_TOPMOST, 0, 0, 0, 0,
							SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
				}
			}
			catch (OperationCanceledException) { }
		}

		private async Task RunConsoleMonitorAsync(CancellationToken ct)
		{
			string consolePath = Path.Combine(_gamePath!, ConsoleLogRelPath);

			// Start from end of file to skip stale messages from previous sessions
			long filePos = File.Exists(consolePath) ? new FileInfo(consolePath).Length : 0;

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
						using var fs = new FileStream(consolePath,
							FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

						fs.Seek(filePos <= fs.Length ? filePos : 0, SeekOrigin.Begin);

						using var reader = new StreamReader(fs);
						string? line;
						while ((line = reader.ReadLine()) != null)
							if (!string.IsNullOrWhiteSpace(line))
								HandleConsoleLine(line);

						filePos = fs.Position;
					}
					catch (IOException) { /* file briefly locked — skip poll */ }

					await Task.Delay(100, ct);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				Console.WriteLine($"[Overlay] Console monitor error: {ex.Message}");
			}
		}

		// -----------------------------------------------------------------------
		// State machine
		// -----------------------------------------------------------------------

		private void TransitionTo(OverlayState next)
		{
			if (_state == next) return;
			_state = next;
			Console.WriteLine($"[Overlay] → {next}");
			StateChanged?.Invoke(next);

			Dispatcher.UIThread.Post(() =>
			{
				if (_window == null) return;
				switch (next)
				{
					case OverlayState.MainMenu:
					case OverlayState.Paused:
						_window.Show();
						break;
					case OverlayState.Hidden:
					case OverlayState.Loading:
					case OverlayState.InGame:
						_window.Hide();
						break;
				}
			});
		}

		private void HandleConsoleLine(string line)
		{
			if (line.Contains(LineMainMenu))
				TransitionTo(OverlayState.MainMenu);
			else if (line.Contains(LinePause))
				TransitionTo(OverlayState.Paused);
			else if (line.Contains(LineLoading) || line.Contains(LineRestoring))
				TransitionTo(OverlayState.Loading);
			else if (line.Contains(LineAchievement))
			{
				var parts = line.Split("[GameMenu] give_achievement ", 2,
					StringSplitOptions.None);
				if (parts.Length > 1)
					AchievementReceived?.Invoke(parts[1].Trim());
			}
		}

		// -----------------------------------------------------------------------
		// Game command sending
		// -----------------------------------------------------------------------

		public async Task SendCommandAsync(string consoleCommand)
		{
			if (string.IsNullOrEmpty(_gamePath))
				throw new InvalidOperationException("OverlayService is not initialised.");

			string lua  = $"SendToConsole(\"{EscapeLua(consoleCommand)}\")";
			string path = Path.Combine(_gamePath, LuaExecRelPath);

			await File.WriteAllTextAsync(path, lua);
			_helper.ExecuteCommand("exec", "HLA-NoVR-Launcher-Helper.exe");
		}

		private static string EscapeLua(string s) =>
			s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

		// -----------------------------------------------------------------------
		// Dispose
		// -----------------------------------------------------------------------

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			_cts?.Cancel();
			_cts?.Dispose();

			Dispatcher.UIThread.Post(() => _window?.Close());
		}
	}
}
