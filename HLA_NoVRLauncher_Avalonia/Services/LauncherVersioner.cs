using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	public class LauncherVersioner
	{
		// Files that exist in vanilla HLA and get modified by the mod
		// — these need to be backed up before install and restored on uninstall
		private static readonly HashSet<string> FilesToBackup = new(
			StringComparer.OrdinalIgnoreCase)
		{
			"game/hlvr/cfg/skill_manifest.cfg",
			"game/hlvr/gameinfo.gi"
		};

		// Files/folders that are purely added by the mod
		// — these get deleted on uninstall
		private static readonly List<string> ModOnlyFiles = new()
		{
			"game/hlvr/scripts/vscripts/bindings.lua",
			"game/hlvr/scripts/vscripts/check_useextra_distance.lua",
			"game/hlvr/scripts/vscripts/crafting_station.lua",
			"game/hlvr/scripts/vscripts/drop_object.lua",
			"game/hlvr/scripts/vscripts/flashlight.lua",
			"game/hlvr/scripts/vscripts/gravity_gloves.lua",
			"game/hlvr/scripts/vscripts/hudhearts.lua",
			"game/hlvr/scripts/vscripts/jumpfix.lua",
			"game/hlvr/scripts/vscripts/main_menu_exec.lua",
			"game/hlvr/scripts/vscripts/multitool.lua",
			"game/hlvr/scripts/vscripts/novr.lua",
			"game/hlvr/scripts/vscripts/novr_config.lua",
			"game/hlvr/scripts/vscripts/novr_precache.lua",
			"game/hlvr/scripts/vscripts/storage.lua",
			"game/hlvr/scripts/vscripts/useextra.lua",
			"game/hlvr/scripts/vscripts/version.lua",
			"game/hlvr/scripts/vscripts/viewmodels.lua",
			"game/hlvr/scripts/vscripts/viewmodels_animation.lua",
			"game/hlvr/scripts/vscripts/viewmodels_precache.lua",
			"game/hlvr/scripts/vscripts/vortenergyhit.lua",
			"game/hlvr/scripts/vscripts/wristpockets.lua",
			"game/hlvr/scripts/vscripts/wristpockets_precache.lua",
		};

		// Entire folders added by the mod — deleted on uninstall
		private static readonly List<string> ModOnlyFolders = new()
		{
			"game/hlvr_addons/novr",
			"game/novr",
			"game/novr_steamdeck",
			"game/novr_viewmodels"
		};

		public static string BackupPath(string gamePath, string backupLocation) =>
			backupLocation switch
			{
				"Game Folder" => Path.Combine(gamePath, ".novr_launcher_backup"),
				"AppData" => Path.Combine(
									 Environment.GetFolderPath(
										 Environment.SpecialFolder.ApplicationData),
									 "HLA_NoVRLauncher", "backup"),
				_ => Path.Combine(
									 AppDomain.CurrentDomain.BaseDirectory,
									 ".novr_launcher_backup")
			};

		/// <summary>
		/// Reads the installed mod version string from the local version.lua file.
		/// </summary>
		public string? GetInstalledModVersion(string gamePath)
		{
			string versionFile = Path.Combine(gamePath,
				"game", "hlvr", "scripts", "vscripts", "version.lua");

			if (!File.Exists(versionFile))
				return null;

			try
			{
				foreach (var line in File.ReadAllLines(versionFile))
				{
					if (line.Contains("NoVR Version:"))
					{
						int start = line.IndexOf("NoVR Version:") +
									"NoVR Version:".Length;
						int end = line.LastIndexOf('"');
						if (start > 0 && end > start)
							return line.Substring(start, end - start).Trim();
					}
				}
			}
			catch { }

			return null;
		}

		/// <summary>
		/// Reads the mod branch and version from inside a zip file.
		/// </summary>
		public (string? version, string? branch) ReadZipInfo(string zipPath)
		{
			try
			{
				using var archive = ZipFile.OpenRead(zipPath);

				foreach (var entry in archive.Entries)
				{
					if (!entry.FullName.EndsWith("version.lua",
						StringComparison.OrdinalIgnoreCase))
						continue;

					using var stream = entry.Open();
					using var reader = new StreamReader(stream);
					string content = reader.ReadToEnd();

					foreach (var line in content.Split('\n'))
					{
						if (!line.Contains("NoVR Version:")) continue;

						int start = line.IndexOf("NoVR Version:") +
									"NoVR Version:".Length;
						int end = line.LastIndexOf('"');
						if (start <= 0 || end <= start) continue;

						string versionStr = line.Substring(
							start, end - start).Trim();
						var parts = versionStr.Split(' ');
						string branch = parts.Length > 0
							? parts[^1].ToLowerInvariant()
							: "main";

						return (versionStr, branch);
					}
				}
			}
			catch { }

			return (null, null);
		}

		/// <summary>
		/// Installs the mod from a local zip file into the game directory.
		/// Strips the top-level folder from the zip (e.g. HLA-NoVR-main/)
		/// and backs up vanilla files before overwriting them.
		/// </summary>
		public void InstallFromZip(
			string zipPath,
			string gamePath,
			string backupLocation,
			IProgress<double> onProgress,
			Action<string> onStatus,
			Action<string> onError)
		{
			try
			{
				if (!File.Exists(zipPath))
				{
					onError("Zip file not found.");
					return;
				}

				if (!Directory.Exists(gamePath))
				{
					onError($"Game path not found: {gamePath}");
					return;
				}

				onStatus("Reading zip file...");
				using var archive = ZipFile.OpenRead(zipPath);

				// Detect the top-level folder prefix to strip
				// e.g. "HLA-NoVR-steam_deck/" 
				string prefix = "";
				var firstEntry = archive.Entries.FirstOrDefault();
				if (firstEntry != null)
				{
					var parts = firstEntry.FullName.Split('/');
					if (parts.Length > 1)
						prefix = parts[0] + "/";
				}

				// Back up vanilla files before overwriting
				onStatus("Backing up original game files...");
				string backupDir = BackupPath(gamePath, backupLocation);
				Directory.CreateDirectory(backupDir);

				foreach (var fileToBackup in FilesToBackup)
				{
					string sourcePath = Path.Combine(gamePath,
						fileToBackup.Replace('/', Path.DirectorySeparatorChar));
					string backupFilePath = Path.Combine(backupDir,
						fileToBackup.Replace('/', Path.DirectorySeparatorChar));

					if (!File.Exists(sourcePath)) continue;
					// Only back up if we don't already have a backup
					if (File.Exists(backupFilePath)) continue;

					Directory.CreateDirectory(
						Path.GetDirectoryName(backupFilePath)!);
					File.Copy(sourcePath, backupFilePath);
				}

				// Extract files, skipping the top-level folder and
				// non-game files like README, LICENSE, .gitattributes
				onStatus("Installing mod files...");
				var gameEntries = archive.Entries
					.Where(e => !string.IsNullOrEmpty(e.Name) &&
								e.FullName.StartsWith(prefix + "game/",
									StringComparison.OrdinalIgnoreCase))
					.ToList();

				int total = gameEntries.Count;
				int current = 0;

				foreach (var entry in gameEntries)
				{
					// Strip the top-level prefix to get the relative path
					string relativePath = entry.FullName.Substring(prefix.Length);
					string destPath = Path.Combine(gamePath,
						relativePath.Replace('/', Path.DirectorySeparatorChar));

					Directory.CreateDirectory(
						Path.GetDirectoryName(destPath)!);
					entry.ExtractToFile(destPath, overwrite: true);

					current++;
					onProgress.Report((double)current / total);
				}

				onStatus("Mod installed successfully!");
			}
			catch (Exception ex)
			{
				onError($"Installation failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Uninstalls the mod by deleting mod-only files and folders,
		/// then restoring backed-up vanilla files.
		/// </summary>
			public void UninstallMod(
			string gamePath,
			string backupLocation,
			Action<string> onStatus,
			Action<string> onError)
		{
			try
			{
				// Delete mod-only files
				onStatus("Removing mod files...");
				foreach (var file in ModOnlyFiles)
				{
					string fullPath = Path.Combine(gamePath,
						file.Replace('/', Path.DirectorySeparatorChar));
					if (File.Exists(fullPath))
						File.Delete(fullPath);
				}

				// Delete mod-only folders
				onStatus("Removing mod folders...");
				foreach (var folder in ModOnlyFolders)
				{
					string fullPath = Path.Combine(gamePath,
						folder.Replace('/', Path.DirectorySeparatorChar));
					if (Directory.Exists(fullPath))
						Directory.Delete(fullPath, recursive: true);
				}

				// Restore backed-up vanilla files
				string backupDir = BackupPath(gamePath, backupLocation);
				if (Directory.Exists(backupDir))
				{
					onStatus("Restoring original game files...");
					foreach (var fileToRestore in FilesToBackup)
					{
						string backupFilePath = Path.Combine(backupDir,
							fileToRestore.Replace('/',
								Path.DirectorySeparatorChar));
						string destPath = Path.Combine(gamePath,
							fileToRestore.Replace('/',
								Path.DirectorySeparatorChar));

						if (!File.Exists(backupFilePath)) continue;
						Directory.CreateDirectory(
							Path.GetDirectoryName(destPath)!);
						File.Copy(backupFilePath, destPath, overwrite: true);
					}

					// Clean up backup folder
					Directory.Delete(backupDir, recursive: true);
				}

				onStatus("Mod uninstalled successfully!");
			}
			catch (Exception ex)
			{
				onError($"Uninstall failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Detects the installed mod branch from the local version.lua.
		/// </summary>
		public string? GetInstalledBranch(string gamePath)
		{
			string? version = GetInstalledModVersion(gamePath);
			if (version == null) return null;
			var parts = version.Split(' ');
			return parts.Length > 0 ? parts[^1].ToLowerInvariant() : "main";
		}
	}
}