using HLA_NoVRLauncher_Avalonia.Models;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	public class SettingsService
	{
		private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.json");
		/// <summary>
		/// Loads the launcher settings. If the file is missing or corrupt, returns a default settings object.
		/// </summary>
		/// <returns>A LauncherSettings object containing stored or default user preferences.</returns>
		public LauncherSettings LoadSettings()
		{
			try
			{
				if (!File.Exists(_configPath))
				{
					return new LauncherSettings();
				}

				string json = File.ReadAllText(_configPath);
				return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
			}
			catch (Exception)
			{
				// If the JSON is malformed, return defaults to prevent a crash
				return new LauncherSettings();
			}
		}

		/// <summary>
		/// Saves the current LauncherSettings object to a JSON file on disk.
		/// </summary>
		/// <param name="settings">The settings object to be serialized and saved.</param>
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
				// In a real app, you might log this error to a file
				Console.WriteLine($"Failed to save settings: {ex.Message}");
			}
		}
	}
}

	public class GameService
	{
		private const string AppId = "546560";
		private const string GameFolderName = "Half-Life Alyx";
		private const string ExecutableSubPath = "game/bin/win64/hlvr.exe";

		/// <summary>
		/// Attempts to locate the Steam installation directory via the Windows Registry.
		/// </summary>
		public string? GetSteamInstallPath()
		{
			using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			return key?.GetValue("SteamPath")?.ToString()?.Replace('/', Path.DirectorySeparatorChar);
		}

		/// <summary>
		/// Locates the game directory within the provided Steam installation path.
		/// </summary>
		public string GetDefaultGamePath(string steamPath)
		{
			return Path.Combine(steamPath, "steamapps", "common", GameFolderName);
		}

		/// <summary>
		/// Validates that the game executable exists at the specified path.
		/// </summary>
		public bool IsGameInstalled(string gamePath)
		{
			if (string.IsNullOrWhiteSpace(gamePath)) return false;
			return File.Exists(Path.Combine(gamePath, ExecutableSubPath));
		}

		/// <summary>
		/// Checks for the existence of specific NoVR mod files.
		/// </summary>
		public bool IsNoVrModInstalled(string gamePath, string[] requiredFiles)
		{
			foreach (var file in requiredFiles)
			{
				if (!File.Exists(Path.Combine(gamePath, file))) return false;
			}
			return true;
		}

		/// <summary>
		/// Formats a Steam browser URL to trigger a game integrity check.
		/// </summary>
		public string GetVerifyLink() => $"steam://validate/{AppId}";
	}

	public class LaunchService
	{
		private const string ExecutableSubPath = "game/bin/win64/hlvr.exe";

		/// <summary>
		/// Launches the game with specific NoVR arguments and monitors the process.
		/// </summary>
		public Process? LaunchGame(string gamePath, string extraArgs, Action onExited)
		{
			string exePath = Path.Combine(gamePath, ExecutableSubPath);
			if (!File.Exists(exePath)) return null;

			ProcessStartInfo startInfo = new()
			{
				FileName = exePath,
				Arguments = $"-novr -verbose {extraArgs}".Trim(),
				WorkingDirectory = Path.GetDirectoryName(exePath),
				UseShellExecute = false
			};

			Process? process = Process.Start(startInfo);
			if (process != null)
			{
				process.EnableRaisingEvents = true;
				process.Exited += (s, e) => onExited?.Invoke();
			}
			return process;
		}

		/// <summary>
		/// Forcefully terminates any running instances of the game executable.
		/// </summary>
		public void KillGameProcess()
		{
			foreach (var process in Process.GetProcessesByName("hlvr"))
			{
				try { process.Kill(); } catch { }
			}
		}
	}


