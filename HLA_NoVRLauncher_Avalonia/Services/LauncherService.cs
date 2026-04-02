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

	public class GameService
	{
		private const string AppId = "546560";
		private const string GameFolderName = "Half-Life Alyx";
		private const string ExecutableSubPath = "game/bin/win64/hlvr.exe";

		public string? GetSteamInstallPath()
		{
			using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			return key?.GetValue("SteamPath")?.ToString()
					   ?.Replace('/', Path.DirectorySeparatorChar);
		}

		public string GetDefaultGamePath(string steamPath)
		{
			return Path.Combine(steamPath, "steamapps", "common", GameFolderName);
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

	public class LaunchService
	{
		private const string ExecutableSubPath = "game/bin/win64/hlvr.exe";

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

		public void KillGameProcess()
		{
			foreach (var process in Process.GetProcessesByName("hlvr"))
			{
				try { process.Kill(); } catch { }
			}
		}
	}
}