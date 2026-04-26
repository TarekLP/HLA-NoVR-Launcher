using HLA_NoVRLauncher_Avalonia.Models;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	/// <summary>
	/// Handles launcher settings persistence and loading.
	/// </summary>
	public class SettingsService
	{
		private readonly string _configPath = Path.Combine(
			AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.json");

		public LauncherSettings LoadSettings()
		{
			try
			{
				if (!File.Exists(_configPath))
					return new LauncherSettings();

				string json = File.ReadAllText(_configPath);
				return JsonSerializer.Deserialize<LauncherSettings>(json)
					   ?? new LauncherSettings();
			}
			catch
			{
				return new LauncherSettings();
			}
		}

		public void SaveSettings(LauncherSettings settings)
		{
			try
			{
				var options = new JsonSerializerOptions { WriteIndented = true };
				string json = JsonSerializer.Serialize(settings, options);
				File.WriteAllText(_configPath, json);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to save settings: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Manages game discovery, installation detection, and Steam integration.
	/// </summary>
	public class GameService
	{
		private const string AppId = "546560";
		private const string GameFolderName = "Half-Life Alyx";
		private const string ExecutableSubPath = "game/bin/win64/hlvr.exe";

		public string? GetSteamInstallPath()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
				return key?.GetValue("SteamPath")?.ToString()
						   ?.Replace('/', Path.DirectorySeparatorChar);
			}

			string[] linuxPaths =
			[
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam"),
			];

			foreach (var path in linuxPaths)
			{
				if (Directory.Exists(path))
					return path;
			}

			return null;
		}

		public string GetDefaultGamePath(string steamPath)
		{
			string defaultPath = Path.Combine(steamPath, "steamapps", "common", GameFolderName);
			if (Directory.Exists(defaultPath))
				return defaultPath;

			string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
			if (File.Exists(vdfPath))
			{
				foreach (var line in File.ReadAllLines(vdfPath))
				{
					if (!line.TrimStart().StartsWith("\"path\""))
						continue;

					var parts = line.Split('"');
					if (parts.Length < 4) continue;

					string libraryPath = parts[3].Replace("\\\\", "\\");
					string gamePath = Path.Combine(
						libraryPath, "steamapps", "common", GameFolderName);

					if (Directory.Exists(gamePath))
						return gamePath;
				}
			}

			return defaultPath;
		}

		public bool IsGameInstalled(string gamePath)
		{
			if (string.IsNullOrWhiteSpace(gamePath)) return false;
			return File.Exists(Path.Combine(gamePath, ExecutableSubPath));
		}

		public bool IsNoVrModInstalled(string gamePath, string[] requiredFiles)
		{
			foreach (var file in requiredFiles)
			{
				if (!File.Exists(Path.Combine(gamePath, file))) return false;
			}
			return true;
		}

		public string GetVerifyLink() => $"steam://validate/{AppId}";
	}

	/// <summary>
	/// Handles game launch with configurable settings and process monitoring.
	///
	/// Launch presets:
	///   "Standard" — applies only the options the user configured manually.
	///   "Debug"    — Standard + -console -vconsole for full logging.
	/// </summary>
	public class LaunchService
	{
		private const string AppId = "546560";

		// Core args always present regardless of preset.
		//
		// -condebug   : Required. novr.lua calls GlobalSys:CommandLineCheck("-condebug")
		//               and shows "The game needs to be started from the launcher!" if
		//               it's missing. This is the sole launcher detection mechanism.
		//
		// -window     : Required for the overlay. Fullscreen exclusive hands the GPU
		//               directly to the game — no other window can appear on top of it
		//               regardless of topmost flags. Windowed mode is mandatory.
		//
		// -noborder   : Removes the window chrome so windowed looks like fullscreen.
		//
		// +hlvr_main_menu_delay* : Suppresses the game's own built-in menu so our
		//               C# overlay can take over menu rendering entirely.
		private const string BaseArgs =
			"-novr +sc_no_cull 1 +sv_cheats 1 +sc_force_lod_level 0 " +
			"+vr_expand_cull_frustum 360 +vr_enable_fake_vr 1 +vr_shadow_map_culling 0 " +
			"-condebug -fullscreen -noborder ";

		private const string LauncherMarkerRelPath = "game/hlvr/scripts/vscripts/main_menu_exec.lua";
		private const string LauncherMarkerContent  = "-- HLA-NoVR Launcher\n";
		private const string ExecutableSubPath = "game/bin/win64/hlvr.exe";

		public void LaunchGame(string extraArgs, Action onExited, LauncherSettings settings)
		{
			var args = new StringBuilder(BaseArgs);
			WriteLauncherMarker(settings.GamePath);

			if (!settings.DefaultMenu)
				args.Append(" +hlvr_main_menu_delay 999999 +hlvr_main_menu_delay_with_intro 999999 " +
							"+hlvr_main_menu_delay_with_intro_and_saves 999999");

			if (settings.DefaultMenu)
				args.Append(" -defaultmenu");

			if (settings.VSync)
				args.Append(" -vsync");

			if (settings.LaunchPreset == "Debug")
				args.Append(" -console -vconsole");

			if (!string.IsNullOrWhiteSpace(extraArgs))
				args.Append($" {extraArgs.Trim()}");

			if (settings.LaunchMethod == "Direct")
			{
				// Launch hlvr.exe directly — bypasses Steam's custom args confirmation
				// dialog and launches faster. Requires game to already be installed.
				string exePath = Path.Combine(
					settings.GamePath,
					ExecutableSubPath.Replace('/', Path.DirectorySeparatorChar));

				Process.Start(new ProcessStartInfo
				{
					FileName = exePath,
					Arguments = args.ToString(),
					UseShellExecute = false,
					WorkingDirectory = Path.GetDirectoryName(exePath)
				});
			}
			else
			{
				// Steam URI — lets Steam handle the launch, shows the custom args
				// confirmation dialog if args are non-standard.
				string uri = $"steam://run/{AppId}//{Uri.EscapeDataString(args.ToString())}";
				Process.Start(new ProcessStartInfo
				{
					FileName = uri,
					UseShellExecute = true
				});
			}

			Task.Run(async () =>
			{
				var deadline = DateTime.UtcNow.AddSeconds(60);
				while (DateTime.UtcNow < deadline)
				{
					var procs = Process.GetProcessesByName("hlvr");
					if (procs.Length > 0)
					{
						await procs[0].WaitForExitAsync();
						onExited?.Invoke();
						return;
					}
					await Task.Delay(2000);
				}
				onExited?.Invoke();
			});
		}

		/// <summary>
		/// Writes a valid Lua comment to main_menu_exec.lua before launch.
		/// novr.lua uses GlobalSys:CommandLineCheck("-condebug") as the primary
		/// launcher check, but having this file present ensures SendToConsole
		/// commands work from the very first frame.
		/// </summary>
		private void WriteLauncherMarker(string gamePath)
		{
			if (string.IsNullOrEmpty(gamePath)) return;

			try
			{
				string path = Path.Combine(
					gamePath,
					LauncherMarkerRelPath.Replace('/', Path.DirectorySeparatorChar));

				// Only write if the directory exists (mod is installed)
				if (Directory.Exists(Path.GetDirectoryName(path)))
					File.WriteAllText(path, LauncherMarkerContent);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[LaunchService] Could not write launcher marker: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Provides cross-platform window management and helper process execution.
	/// Supports Windows (Win32 API) and Linux (X11).
	/// </summary>
	public class LauncherHelperService
	{
		private static class WindowHelper
		{
			// ============ Windows P/Invoke Imports ============

			[DllImport("user32.dll", SetLastError = true)]
			public static extern IntPtr FindWindowA(string lpClassName, string lpWindowName);

			[DllImport("user32.dll")]
			private static extern IntPtr GetTopWindow(IntPtr hWnd);

			[DllImport("user32.dll")]
			private static extern IntPtr GetNextWindow(IntPtr hWnd, int nCmd);

			private const int GW_HWNDNEXT = 2;

			[DllImport("user32.dll")]
			private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

			[DllImport("user32.dll")]
			private static extern bool IsWindowVisible(IntPtr hWnd);

			[DllImport("kernel32.dll")]
			private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

			private const uint TH32CS_SNAPPROCESS = 2;

			[DllImport("kernel32.dll")]
			private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

			[DllImport("kernel32.dll")]
			private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

			[DllImport("kernel32.dll")]
			private static extern bool CloseHandle(IntPtr hObject);

			[DllImport("user32.dll")]
			private static extern bool SetForegroundWindow(IntPtr hWnd);

			[DllImport("user32.dll")]
			private static extern bool SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

			private const int GWLP_HWNDPARENT = -8;

			[DllImport("user32.dll")]
			private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

			[DllImport("user32.dll")]
			private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

			[DllImport("user32.dll")]
			private static extern short GetKeyState(int nVirtKey);

			private const int VK_ESCAPE = 0x1B;

			[DllImport("user32.dll")]
			private static extern bool SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

			private const uint WM_KEYDOWN = 0x0100;
			private const uint WM_KEYUP   = 0x0101;
			private const int  VK_PAUSE   = 0x13;

			// ============ Windows Data Structures ============

			[StructLayout(LayoutKind.Sequential)]
			private struct ProcessEntry32
			{
				public uint dwSize;
				public uint cntUsage;
				public uint th32ProcessID;
				public IntPtr th32ParentProcessID;
				public uint th32ModuleID;
				public uint cntThreads;
				public uint th32ParentProcessID_2;
				public int pcPriority;
				[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
				public string szExeFile;
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct Rect
			{
				public int left;
				public int top;
				public int right;
				public int bottom;
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct Point
			{
				public int x;
				public int y;
			}

			public static IntPtr GetWindowFromProcessID(uint processID)
			{
				IntPtr hwnd = GetTopWindow(IntPtr.Zero);
				while (hwnd != IntPtr.Zero)
				{
					GetWindowThreadProcessId(hwnd, out uint wndProcID);
					if (wndProcID == processID && IsWindowVisible(hwnd))
						return hwnd;
					hwnd = GetNextWindow(hwnd, GW_HWNDNEXT);
				}
				return IntPtr.Zero;
			}

			public static uint GetProcessIDByExeName(string exeName)
			{
				IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
				if (snapshot == IntPtr.Zero)
					return 0;

				try
				{
					var pe = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf(typeof(ProcessEntry32)) };
					if (Process32First(snapshot, ref pe))
					{
						do
						{
							if (exeName.Equals(pe.szExeFile, StringComparison.OrdinalIgnoreCase))
								return pe.th32ProcessID;
						} while (Process32Next(snapshot, ref pe));
					}
				}
				finally
				{
					CloseHandle(snapshot);
				}

				return 0;
			}

			public static IntPtr GetWindowByExeName(string exeName)
			{
				uint processID = GetProcessIDByExeName(exeName);
				return processID == 0 ? IntPtr.Zero : GetWindowFromProcessID(processID);
			}

			public static void FocusWindow(IntPtr hwnd)
			{
				if (hwnd != IntPtr.Zero)
					SetForegroundWindow(hwnd);
			}

			public static void SetParent(IntPtr childWindow, IntPtr parentWindow)
			{
				if (childWindow != IntPtr.Zero && parentWindow != IntPtr.Zero)
					SetWindowLongPtr(childWindow, GWLP_HWNDPARENT, parentWindow);
			}

			public static void SendPauseKey(IntPtr hwnd)
			{
				if (hwnd != IntPtr.Zero)
				{
					SendMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_PAUSE, IntPtr.Zero);
					SendMessage(hwnd, WM_KEYUP,   (IntPtr)VK_PAUSE, IntPtr.Zero);
				}
			}

			public static (int x, int y, int width, int height) GetWindowGeometry(IntPtr hwnd)
			{
				if (hwnd == IntPtr.Zero)
					return (0, 0, 0, 0);

				GetClientRect(hwnd, out Rect rect);
				var topLeft     = new Point { x = rect.left,  y = rect.top    };
				var bottomRight = new Point { x = rect.right, y = rect.bottom };

				ClientToScreen(hwnd, ref topLeft);
				ClientToScreen(hwnd, ref bottomRight);

				int width  = bottomRight.x - topLeft.x;
				int height = bottomRight.y - topLeft.y;

				return (topLeft.x, topLeft.y, width, height);
			}

			public static bool IsEscapePressed()
			{
				return (GetKeyState(VK_ESCAPE) & 0x8000) != 0;
			}

			[DllImport("user32.dll", SetLastError = true)]
			private static extern bool SetWindowPos(
				IntPtr hWnd, IntPtr hWndInsertAfter,
				int x, int y, int cx, int cy,
				uint uFlags);

			private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

			private const uint SWP_NOMOVE     = 0x0002;
			private const uint SWP_NOSIZE     = 0x0001;
			private const uint SWP_SHOWWINDOW = 0x0040;

			public static void ForceTopmost(IntPtr hwnd)
			{
				if (hwnd == IntPtr.Zero) return;
				SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
					SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
			}
		}

		private const int WINDOW_WAIT_TIMEOUT_SECONDS = 120;
		private const int WINDOW_CHECK_INTERVAL_MS    = 10;
		private const int GEOMETRY_UPDATE_INTERVAL_MS = 10;

		/// <summary>
		/// Executes launcher helper commands: exec, focusgame, focuslauncher, update.
		/// </summary>
		public void ExecuteCommand(string command, string helperExecutableName)
		{
			try
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					ExecuteCommandWindows(command, helperExecutableName);
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
					ExecuteCommandLinux(command);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error executing command '{command}': {ex.Message}");
			}
		}

		private void ExecuteCommandWindows(string command, string helperExecutableName)
		{
			switch (command.ToLower())
			{
				case "exec":
					WindowHelper.SendPauseKey(WindowHelper.GetWindowByExeName("hlvr.exe"));
					break;

				case "focusgame":
					WindowHelper.FocusWindow(WindowHelper.GetWindowByExeName("hlvr.exe"));
					break;

				case "focuslauncher":
					WindowHelper.FocusWindow(
						WindowHelper.FindWindowA("Engine", "Half-Life: Alyx NoVR Launcher"));
					break;

				case "update":
					HandleUpdate("HLA-NoVR-Launcher.exe");
					break;
			}
		}

		private void ExecuteCommandLinux(string command)
		{
			Console.WriteLine($"Linux command '{command}' - X11 support requires additional implementation");
		}

		private void HandleUpdate(string launcherExePath)
		{
			try
			{
				string updatePath = launcherExePath + ".update";

				int maxAttempts = 10;
				int attempt = 0;
				while (File.Exists(launcherExePath) && attempt < maxAttempts)
				{
					try   { File.Delete(launcherExePath); break; }
					catch { attempt++; Thread.Sleep(100); }
				}

				if (File.Exists(updatePath))
					File.Move(updatePath, launcherExePath, true);

				Process.Start(new ProcessStartInfo
				{
					FileName = launcherExePath,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Update failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Monitors game window geometry periodically. Fires the callback only
		/// when the geometry actually changes, keeping CPU usage minimal.
		/// </summary>
		public async Task MonitorGameWindowAsync(
			IntPtr gameWindow,
			Action<(int x, int y, int width, int height)> onGeometryChanged,
			CancellationToken cancellationToken = default)
		{
			if (gameWindow == IntPtr.Zero)
				throw new ArgumentException("Invalid game window handle");

			(int lastX, int lastY, int lastW, int lastH) = (0, 0, 0, 0);

			while (!cancellationToken.IsCancellationRequested)
			{
				var (x, y, w, h) = WindowHelper.GetWindowGeometry(gameWindow);

				if (x != lastX || y != lastY || w != lastW || h != lastH)
				{
					onGeometryChanged((x, y, w, h));
					(lastX, lastY, lastW, lastH) = (x, y, w, h);
				}

				await Task.Delay(GEOMETRY_UPDATE_INTERVAL_MS, cancellationToken);
			}
		}

		/// <summary>
		/// Waits for the game window to appear, with timeout.
		/// </summary>
		public async Task<IntPtr> WaitForGameWindowAsync(
			string gameExecutableName,
			IntPtr menuWindow = default,
			bool setupParent = false,
			CancellationToken cancellationToken = default)
		{
			DateTime startTime = DateTime.UtcNow;

			while (!cancellationToken.IsCancellationRequested)
			{
				if ((DateTime.UtcNow - startTime).TotalSeconds > WINDOW_WAIT_TIMEOUT_SECONDS)
					throw new TimeoutException(
						$"Game window not found after {WINDOW_WAIT_TIMEOUT_SECONDS} seconds");

				IntPtr gameWindow = WindowHelper.GetWindowByExeName(gameExecutableName);

				if (gameWindow != IntPtr.Zero)
				{
					if (setupParent && menuWindow != IntPtr.Zero)
					{
						WindowHelper.SetParent(menuWindow, gameWindow);
						WindowHelper.FocusWindow(gameWindow);
					}
					return gameWindow;
				}

				await Task.Delay(WINDOW_CHECK_INTERVAL_MS, cancellationToken);
			}

			throw new OperationCanceledException("Waiting for game window was cancelled");
		}

		/// <summary>Finds a window by class name and title (Windows only).</summary>
		public IntPtr FindWindowByName(string className, string windowTitle)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				throw new PlatformNotSupportedException("FindWindowByName is Windows-only");

			return WindowHelper.FindWindowA(className, windowTitle);
		}

		/// <summary>Gets the game process by executable name.</summary>
		public Process? GetGameProcess(string gameExecutableName)
		{
			var processes = Process.GetProcessesByName(gameExecutableName.Replace(".exe", ""));
			return processes.Length > 0 ? processes[0] : null;
		}

		/// <summary>
		/// Returns the current position and size of a window in screen pixels.
		/// </summary>
		public (int x, int y, int width, int height) GetWindowGeometry(IntPtr hwnd)
			=> WindowHelper.GetWindowGeometry(hwnd);

		/// <summary>
		/// Forces a window to be always-on-top using Win32 SetWindowPos.
		/// </summary>
		public void SetTopmost(IntPtr hwnd)
			=> WindowHelper.ForceTopmost(hwnd);

		/// <summary>
		/// Makes childHwnd an owned window of parentHwnd so it stays above it
		/// in Win32 Z-order.
		/// </summary>
		public void SetParent(IntPtr childHwnd, IntPtr parentHwnd)
			=> WindowHelper.SetParent(childHwnd, parentHwnd);
	}
}
