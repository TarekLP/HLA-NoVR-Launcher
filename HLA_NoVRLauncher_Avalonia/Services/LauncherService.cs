using HLA_NoVRLauncher_Avalonia.Models;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
			// Windows — registry
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
				return key?.GetValue("SteamPath")?.ToString()
						   ?.Replace('/', Path.DirectorySeparatorChar);
			}

			// Linux / Steam Deck — standard paths
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
		private const string AppId = "546560";

		public void LaunchGame(string extraArgs, Action onExited)
		{
			string args = $"-novr -console -vconsole +sc_no_cull 1 +sv_cheats 1 +sc_force_lod_level 0 +vr_expand_cull_frustum 360 +vr_enable_fake_vr 1 {extraArgs}".Trim();
			string uri = $"steam://run/{AppId}//{Uri.EscapeDataString(args)}";

			Process.Start(new ProcessStartInfo
			{
				FileName = uri,
				UseShellExecute = true
			});

			System.Threading.Tasks.Task.Run(async () =>
			{
				await System.Threading.Tasks.Task.Delay(5000);

				while (true)
				{
					var procs = Process.GetProcessesByName("hlvr");
					if (procs.Length > 0)
					{
						await procs[0].WaitForExitAsync();
						break;
					}
					await System.Threading.Tasks.Task.Delay(2000);
				}

				onExited?.Invoke();
			});
		}
	}
}