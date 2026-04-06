using System;
using System.IO;
using System.IO.Compression;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	public class LauncherVersioner
	{
		private const string VersionFilePath = "game/hlvr/scripts/vscripts/version.lua";

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
						int start = line.IndexOf("NoVR Version:") + "NoVR Version:".Length;
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
		/// Reads the mod branch from a zip file by checking version.lua inside it.
		/// Returns null if the zip doesn't contain a valid version.lua.
		/// </summary>
		public (string? version, string? branch) ReadZipInfo(string zipPath)
		{
			try
			{
				using var archive = ZipFile.OpenRead(zipPath);

				// Find version.lua anywhere in the zip
				foreach (var entry in archive.Entries)
				{
					if (!entry.FullName.EndsWith("version.lua",
						StringComparison.OrdinalIgnoreCase))
						continue;

					using var stream = entry.Open();
					using var reader = new StreamReader(stream);
					string content = reader.ReadToEnd();

					string? version = null;
					string? branch = null;

					foreach (var line in content.Split('\n'))
					{
						if (line.Contains("NoVR Version:"))
						{
							int start = line.IndexOf("NoVR Version:") +
										"NoVR Version:".Length;
							int end = line.LastIndexOf('"');
							if (start > 0 && end > start)
							{
								string versionStr = line.Substring(
									start, end - start).Trim();

								// Branch is the last word e.g. "Jan 04 15:08 mods"
								var parts = versionStr.Split(' ');
								version = versionStr;
								branch = parts.Length > 0
									? parts[^1].ToLowerInvariant()
									: "main";
							}
						}
					}

					return (version, branch);
				}
			}
			catch { }

			return (null, null);
		}

		/// <summary>
		/// Installs the mod from a local zip file into the game directory.
		/// </summary>
		public void InstallFromZip(
			string zipPath,
			string gamePath,
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
				int total = archive.Entries.Count;
				int current = 0;

				onStatus("Installing mod files...");
				foreach (var entry in archive.Entries)
				{
					// Skip directory entries
					if (string.IsNullOrEmpty(entry.Name))
					{
						current++;
						continue;
					}

					string destPath = Path.Combine(gamePath, entry.FullName
						.Replace('/', Path.DirectorySeparatorChar));

					Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
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