using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	/// <summary>
	/// Manages the in-game overlay menu window and communication with the HLA game.
	/// Handles window positioning, input routing, and game command execution.
	/// </summary>
	public class GameOverlayService : IDisposable
	{
		private Window? _overlayWindow;
		private IntPtr _gameWindowHandle;
		private readonly LauncherHelperService _helperService;
		private CancellationTokenSource? _geometryMonitoringCts;
		private Task? _geometryMonitoringTask;
		private Task? _consoleMonitoringTask;
		private CancellationTokenSource? _consoleMonitoringCts;

		private string? _gamePath;

		// Game communication paths
		private const string LUA_EXEC_PATH = "game/hlvr/scripts/vscripts/main_menu_exec.lua";
		private const string CONSOLE_LOG_PATH = "game/hlvr/console.log";

		// Event for when game sends menu commands
		public event Action<string[]>? MenuCommandReceived;

		// Event for geometry changes (useful for debugging)
		public event Action<(int x, int y, int width, int height)>? GameWindowGeometryChanged;

		// Event when game process exits
		public event Action? GameWindowLost;

		public GameOverlayService(LauncherHelperService helperService)
		{
			_helperService = helperService ?? throw new ArgumentNullException(nameof(helperService));
		}

		/// <summary>
		/// Initializes the overlay and positions it over the game window.
		/// </summary>
		public async Task InitializeAsync(
			string gamePath,
			Func<Window> overlayWindowFactory,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrEmpty(gamePath))
				throw new ArgumentException("Game path cannot be null or empty", nameof(gamePath));

			_gamePath = gamePath;

			// Wait for game window to appear
			_gameWindowHandle = await _helperService.WaitForGameWindowAsync(
				"hlvr.exe",
				cancellationToken: cancellationToken);

			if (_gameWindowHandle == IntPtr.Zero)
				throw new InvalidOperationException("Failed to find game window");

			// Create overlay window
			_overlayWindow = overlayWindowFactory();
			_overlayWindow.Show();

			// Position overlay over game
			UpdateOverlayPosition();

			// Start monitoring game window geometry
			await StartGeometryMonitoringAsync(cancellationToken);

			// Start monitoring game console output
			await StartConsoleMonitoringAsync(cancellationToken);
		}

		/// <summary>
		/// Sends a game command via Lua execution.
		/// Example: SendGameCommandAsync("pause") → executes Lua in game
		/// </summary>
		public async Task SendGameCommandAsync(string command)
		{
			if (string.IsNullOrEmpty(_gamePath))
				throw new InvalidOperationException("Overlay not initialized");

			try
			{
				// Wrap command in Lua function
				string luaCommand = $"SendToConsole(\"{EscapeLuaString(command)}\")";
				string execPath = Path.Combine(_gamePath, LUA_EXEC_PATH);

				// Write Lua command to file
				await File.WriteAllTextAsync(execPath, luaCommand);

				// Tell game to execute it via PAUSE key
				_helperService.ExecuteCommand("exec", "HLA-NoVR-Launcher-Helper.exe");

				await Task.Delay(10); // Small delay for file system sync
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error sending game command: {ex.Message}");
			}
		}

		/// <summary>
		/// Reads a setting from the game config files.
		/// </summary>
		public object? ReadGameSetting(string key)
		{
			if (string.IsNullOrEmpty(_gamePath))
				throw new InvalidOperationException("Overlay not initialized");

			try
			{
				// Try reading from bindings.lua first (for input settings)
				string bindingsPath = Path.Combine(_gamePath, "game/hlvr/scripts/vscripts/bindings.lua");
				if (File.Exists(bindingsPath))
				{
					string content = File.ReadAllText(bindingsPath);
					var value = ParseLuaValue(content, key);
					if (value != null)
						return value;
				}

				// Try personal.cfg (for difficulty, commentary, etc)
				string personalCfgPath = Path.Combine(_gamePath, "game/hlvr/SAVE/personal.cfg");
				if (File.Exists(personalCfgPath))
				{
					string content = File.ReadAllText(personalCfgPath);
					var value = ParseConfigValue(content, key);
					if (value != null)
						return value;
				}

				// Try machine_convars.vcfg (for volume, FOV, etc)
				string convarPath = Path.Combine(_gamePath, "game/hlvr/cfg/machine_convars.vcfg");
				if (File.Exists(convarPath))
				{
					string content = File.ReadAllText(convarPath);
					var value = ParseConfigValue(content, key);
					if (value != null)
						return value;
				}

				return null;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error reading game setting {key}: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Writes a setting to the game config files.
		/// </summary>
		public async Task WriteGameSettingAsync(string key, object value)
		{
			if (string.IsNullOrEmpty(_gamePath))
				throw new InvalidOperationException("Overlay not initialized");

			try
			{
				// Determine which file to write to based on key
				string? filePath = DetermineConfigFileForKey(key);
				if (filePath == null)
					return;

				filePath = Path.Combine(_gamePath, filePath);

				if (!File.Exists(filePath))
					return;

				string content = File.ReadAllText(filePath);
				string newContent = UpdateConfigValue(content, key, value);

				await File.WriteAllTextAsync(filePath, newContent);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error writing game setting {key}: {ex.Message}");
			}
		}

		/// <summary>
		/// Shows the overlay window.
		/// </summary>
		public void Show()
		{
			if (_overlayWindow != null)
				_overlayWindow.Show();
		}

		/// <summary>
		/// Hides the overlay window.
		/// </summary>
		public void Hide()
		{
			if (_overlayWindow != null)
				_overlayWindow.Hide();
		}

		/// <summary>
		/// Toggles overlay visibility.
		/// </summary>
		public void Toggle()
		{
			if (_overlayWindow != null)
			{
				if (_overlayWindow.IsVisible)
					Hide();
				else
					Show();
			}
		}

		/// <summary>
		/// Checks if game window still exists.
		/// </summary>
		public bool IsGameWindowValid()
		{
			var process = _helperService.GetGameProcess("hlvr");
			return process != null && !process.HasExited;
		}

		/// <summary>
		/// Cleans up resources.
		/// </summary>
		public void Dispose()
		{
			_geometryMonitoringCts?.Cancel();
			_consoleMonitoringCts?.Cancel();

			try
			{
				_geometryMonitoringTask?.Wait(TimeSpan.FromSeconds(1));
				_consoleMonitoringTask?.Wait(TimeSpan.FromSeconds(1));
			}
			catch { }

			_geometryMonitoringCts?.Dispose();
			_consoleMonitoringCts?.Dispose();
			_overlayWindow?.Close();
		}

		// ==================== Private Methods ====================

		private void UpdateOverlayPosition()
		{
			if (_overlayWindow == null)
				return;

			try
			{
				(int x, int y, int width, int height) = _helperService.GetWindowGeometry(_gameWindowHandle);

				Dispatcher.UIThread.Post(() =>
				{
					if (_overlayWindow != null)
					{
						_overlayWindow.Position = new PixelPoint(x, y);
						_overlayWindow.Width = width;
						_overlayWindow.Height = height;
					}
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error updating overlay position: {ex.Message}");
			}
		}

		private async Task StartGeometryMonitoringAsync(CancellationToken cancellationToken)
		{
			_geometryMonitoringCts = new CancellationTokenSource();
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _geometryMonitoringCts.Token);

			_geometryMonitoringTask = Task.Run(async () =>
			{
				try
				{
					await _helperService.MonitorGameWindowAsync(
						_gameWindowHandle,
						(int x, int y, int w, int h) =>
						{
							UpdateOverlayPosition();
							GameWindowGeometryChanged?.Invoke((x, y, w, h));
						},
						linkedCts.Token);
				}
				catch (OperationCanceledException)
				{
					// Expected when cancelling
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Geometry monitoring error: {ex.Message}");
					GameWindowLost?.Invoke();
				}
			}, linkedCts.Token);
		}

		private async Task StartConsoleMonitoringAsync(CancellationToken cancellationToken)
		{
			_consoleMonitoringCts = new CancellationTokenSource();
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _consoleMonitoringCts.Token);

			_consoleMonitoringTask = Task.Run(async () =>
			{
				try
				{
					while (!linkedCts.Token.IsCancellationRequested)
					{
						if (string.IsNullOrEmpty(_gamePath))
							break;

						string consolePath = Path.Combine(_gamePath, CONSOLE_LOG_PATH);

						if (!File.Exists(consolePath))
						{
							await Task.Delay(500, linkedCts.Token);
							continue;
						}

						try
						{
							// Read console.log and look for menu commands
							string content = File.ReadAllText(consolePath);
							var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

							foreach (var line in lines)
							{
								if (line.Contains("[GameMenu] "))
								{
									string[] parts = line.Split(new[] { "[GameMenu] " }, StringSplitOptions.None);
									if (parts.Length > 1)
									{
										string commandLine = parts[1].Trim();
										string[] command = commandLine.Split(' ');

										if (command.Length > 0)
										{
											MenuCommandReceived?.Invoke(command);
										}
									}
								}
							}
						}
						catch (IOException)
						{
							// File locked, try again
						}

						await Task.Delay(100, linkedCts.Token);
					}
				}
				catch (OperationCanceledException)
				{
					// Expected when cancelling
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Console monitoring error: {ex.Message}");
				}
			}, linkedCts.Token);
		}

		private string EscapeLuaString(string input)
		{
			// Escape special characters for Lua strings
			return input
				.Replace("\\", "\\\\")
				.Replace("\"", "\\\"")
				.Replace("\n", "\\n")
				.Replace("\r", "\\r");
		}

		private object? ParseLuaValue(string content, string key)
		{
			// Simple Lua parsing for: KEY = "value" or KEY = true/false
			foreach (var line in content.Split(Environment.NewLine))
			{
				string trimmed = line.Replace(" ", "").Replace("\t", "");

				if (trimmed.StartsWith(key + "="))
				{
					string value = trimmed.Substring((key + "=").Length).TrimEnd(';');
					value = value.Trim('"');

					if (bool.TryParse(value, out bool boolVal))
						return boolVal;

					if (float.TryParse(value, out float floatVal))
						return floatVal;

					if (int.TryParse(value, out int intVal))
						return intVal;

					return value;
				}
			}

			return null;
		}

		private object? ParseConfigValue(string content, string key)
		{
			// Parse config files: "key" "value"
			foreach (var line in content.Split(Environment.NewLine))
			{
				if (line.TrimStart().StartsWith("\"" + key + "\""))
				{
					var parts = line.Split('"');
					if (parts.Length >= 4)
					{
						string value = parts[3];

						if (bool.TryParse(value, out bool boolVal))
							return boolVal;

						if (float.TryParse(value, out float floatVal))
							return floatVal;

						if (int.TryParse(value, out int intVal))
							return intVal;

						return value;
					}
				}
			}

			return null;
		}

		private string UpdateConfigValue(string content, string key, object value)
		{
			// Rebuild config file with updated value
			var lines = new List<string>();

			foreach (var line in content.Split(Environment.NewLine))
			{
				// Check if this line contains our key
				if (line.Contains($"\"{key}\""))
				{
					// Find the opening quote of the value
					int firstQuote = line.IndexOf("\"");
					int secondQuote = line.IndexOf("\"", firstQuote + 1);
					int thirdQuote = line.IndexOf("\"", secondQuote + 1);
					int fourthQuote = line.IndexOf("\"", thirdQuote + 1);

					if (thirdQuote != -1 && fourthQuote != -1)
					{
						// Reconstruct line with new value
						string prefix = line.Substring(0, thirdQuote + 1);
						string suffix = line.Substring(fourthQuote);
						lines.Add($"{prefix}{value}{suffix}");
					}
					else
					{
						lines.Add(line);
					}
				}
				else
				{
					lines.Add(line);
				}
			}

			return string.Join(Environment.NewLine, lines);
		}

		private string? DetermineConfigFileForKey(string key)
		{
			// Determine which config file a key belongs to
			var bindingsKeys = new[] { "MOUSE_SENSITIVITY", "INVERT_MOUSE_X", "INVERT_MOUSE_Y", "FOV" };
			var personalCfgKeys = new[] { "setting.skill", "setting.commentary" };
			var convarKeys = new[] { "snd_gain", "snd_gamevolume", "snd_musicvolume", "snd_gamevoicevolume", "fov_desired", "r_light_sensitivity_mode", "hlvr_closed_caption_type" };

			if (Array.Exists(bindingsKeys, element => element == key))
				return "game/hlvr/scripts/vscripts/bindings.lua";

			if (Array.Exists(personalCfgKeys, element => element == key))
				return "game/hlvr/SAVE/personal.cfg";

			if (Array.Exists(convarKeys, element => element == key))
				return "game/hlvr/cfg/machine_convars.vcfg";

			return null;
		}
	}
}