using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HLA_NoVRLauncher_Avalonia.Models;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	public class LauncherVersioner
	{
		private const string GitHubUser = "HLANoVR";
		private const string ModRepo = "HLA-NoVR";
		private const string LauncherRepo = "HLA-NoVR-Launcher";

		private readonly HttpClient _http = new();

		// Launcher Version Checking
		/// <summary>
		/// Fetches the latest launcher release tag from GitHub API.
		/// Returns null if the request fails.
		/// </summary>
		public async Task<string?> GetLatestLauncherVersionAsync()
		{
			try
			{
				string url = $"https://api.github.com/repos/{GitHubUser}/{LauncherRepo}/releases/latest";
				_http.DefaultRequestHeaders.UserAgent.TryParseAdd("HLA-NoVR-Launcher");

				string json = await _http.GetStringAsync(url);
				using var doc = JsonDocument.Parse(json);

				return doc.RootElement
						  .GetProperty("tag_name")
						  .GetString();
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Fetches the latest mod version string from the mod's version.lua file on GitHub.
		/// Returns null if the request fails.
		/// </summary>
		public async Task<string?> GetLatestModVersionAsync(string branch = "main")
		{
			try
			{
				string url = $"https://raw.githubusercontent.com/{GitHubUser}/{ModRepo}/{branch}/game/hlvr/scripts/vscripts/version.lua";
				string content = await _http.GetStringAsync(url);

				// version.lua contains a line like: version = "1.2.3"
				foreach (var line in content.Split('\n'))
				{
					if (line.TrimStart().StartsWith("version"))
					{
						int start = line.IndexOf('"') + 1;
						int end = line.LastIndexOf('"');
						if (start > 0 && end > start)
							return line.Substring(start, end - start);
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Compares the current launcher version against the latest GitHub release.
		/// </summary>
		public async Task<bool> IsLauncherUpdateAvailableAsync(string currentVersion)
		{
			string? latest = await GetLatestLauncherVersionAsync();
			if (latest == null) return false;

			// Strip leading 'v' if present e.g. "v4.2.4" -> "4.2.4"
			latest = latest.TrimStart('v');
			currentVersion = currentVersion.TrimStart('v');

			return latest != currentVersion;
		}

		/// <summary>
		/// Compares the installed mod version against the latest on GitHub.
		/// </summary>
		public async Task<bool> IsModUpdateAvailableAsync(string installedVersion, string branch = "main")
		{
			string? latest = await GetLatestModVersionAsync(branch);
			if (latest == null) return false;
			return latest != installedVersion.TrimStart('v');
		}

		//  Mod Installer / Updater

		/// <summary>
		/// Downloads and installs the latest mod release into the game directory.
		/// Reports progress via the onProgress callback (0.0 to 1.0).
		/// Reports status messages via the onStatus callback.
		/// </summary>
		public async Task InstallOrUpdateModAsync(
			string gamePath,
			string branch,
			IProgress<double> onProgress,
			Action<string> onStatus,
			Action<string> onError)
		{
			try
			{
				// Step 1 — find the download URL for the latest release zip
				onStatus("Fetching latest mod release info...");
				string releaseUrl = $"https://api.github.com/repos/{GitHubUser}/{ModRepo}/releases/latest";
				_http.DefaultRequestHeaders.UserAgent.TryParseAdd("HLA-NoVR-Launcher");

				string releaseJson = await _http.GetStringAsync(releaseUrl);
				using var doc = JsonDocument.Parse(releaseJson);

				string? downloadUrl = null;
				foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
				{
					string? name = asset.GetProperty("name").GetString();
					if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					{
						downloadUrl = asset.GetProperty("browser_download_url").GetString();
						break;
					}
				}

				if (downloadUrl == null)
				{
					onError("Could not find a mod zip in the latest release.");
					return;
				}

				// Step 2 — download the zip with progress reporting
				onStatus("Downloading mod files...");
				string tempZip = Path.Combine(Path.GetTempPath(), "hla_novr_mod.zip");

				using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
				{
					long? totalBytes = response.Content.Headers.ContentLength;
					using var contentStream = await response.Content.ReadAsStreamAsync();
					using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

					byte[] buffer = new byte[81920];
					long totalRead = 0;
					int bytesRead;

					while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
					{
						await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
						totalRead += bytesRead;

						if (totalBytes.HasValue)
							onProgress.Report((double)totalRead / totalBytes.Value);
					}
				}

				onProgress.Report(1.0);

				// Step 3 — extract the zip into the game directory
				onStatus("Installing mod files...");
				if (!Directory.Exists(gamePath))
				{
					onError($"Game path not found: {gamePath}");
					return;
				}

				ZipFile.ExtractToDirectory(tempZip, gamePath, overwriteFiles: true);

				// Step 4 — clean up temp file
				File.Delete(tempZip);

				onStatus("Mod installed successfully!");
			}
			catch (Exception ex)
			{
				onError($"Installation failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Reads the installed mod version from the local version.lua file.
		/// Returns null if not installed or file is missing.
		/// </summary>
		public string? GetInstalledModVersion(string gamePath)
		{
			string versionFile = Path.Combine(
				gamePath, "game", "hlvr", "scripts", "vscripts", "version.lua");

			if (!File.Exists(versionFile))
				return null;

			try
			{
				foreach (var line in File.ReadAllLines(versionFile))
				{
					if (line.TrimStart().StartsWith("version"))
					{
						int start = line.IndexOf('"') + 1;
						int end = line.LastIndexOf('"');
						if (start > 0 && end > start)
							return line.Substring(start, end - start);
					}
				}
			}
			catch { }

			return null;
		}
	}
}