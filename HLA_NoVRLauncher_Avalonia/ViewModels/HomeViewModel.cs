using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class HomeViewModel : ViewModelBase
	{
		private readonly GameService _gameService = new();
		private readonly LaunchService _launchService = new();
		private readonly SettingsService _settingsService = new();
		private readonly LauncherVersioner _versioner = new();

		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
		[NotifyCanExecuteChangedFor(nameof(InstallModCommand))]
		private bool _isBusy;

		[ObservableProperty]
		private LauncherStatus _status = LauncherStatus.CheckingStatus;

		[ObservableProperty]
		private double _installProgress = 0;

		[ObservableProperty]
		private bool _isInstalling;

		[ObservableProperty]
		private string _installStatusMessage = "";

		[ObservableProperty]
		private string _installedModVersion = "";

		public string StatusMessage => _status switch
		{
			LauncherStatus.Ready => "Ready to Launch (NoVR)",
			LauncherStatus.GameRunning => "Game is running...",
			LauncherStatus.MissingFiles => "Half-Life: Alyx not found.",
			LauncherStatus.SteamNotFound => "Steam installation not found.",
			LauncherStatus.CheckingStatus => "Checking game status...",
			LauncherStatus.ModNotInstalled => "Mod not installed — click Install!",
			_ => "Unknown status."
		};

		public string InstallButtonText => _status == LauncherStatus.ModNotInstalled
			? "Install Mod"
			: "Reinstall Mod";

		public bool CanLaunch => !IsBusy;
		public bool CanInstall => !IsBusy;

		public event Action? RequestCloseLauncher;
		public event Action? RequestSetup;

		public HomeViewModel()
		{
			UpdateStatus();
		}

		partial void OnStatusChanged(LauncherStatus value)
		{
			OnPropertyChanged(nameof(StatusMessage));
			OnPropertyChanged(nameof(InstallButtonText));
		}

		private string ResolveGamePath()
		{
			LauncherSettings settings = _settingsService.LoadSettings();
			if (!string.IsNullOrEmpty(settings.GamePath))
				return settings.GamePath;

			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			if (string.IsNullOrEmpty(steamPath))
				return "";

			string detectedPath = _gameService.GetDefaultGamePath(steamPath);
			settings.GamePath = detectedPath;
			_settingsService.SaveSettings(settings);
			return detectedPath;
		}

		[RelayCommand(CanExecute = nameof(CanLaunch))]
		private async Task LaunchGameAsync()
		{
			IsBusy = true;
			Status = LauncherStatus.GameRunning;
			LauncherSettings settings = _settingsService.LoadSettings();
			_settingsService.SaveSettings(settings);

			if (settings.CloseLauncherOnStart)
				RequestCloseLauncher?.Invoke();

			_launchService.LaunchGame(settings.CustomLaunchArgs, () =>
			{
				Dispatcher.UIThread.Post(() =>
				{
					IsBusy = false;
					UpdateStatus();
				});
			}, settings);

			await Task.CompletedTask;
		}

		[RelayCommand(CanExecute = nameof(CanInstall))]
		private async Task InstallModAsync()
		{
			// Open file picker for zip
			var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
				IClassicDesktopStyleApplicationLifetime desktop
					? desktop.MainWindow
					: null;

			if (topLevel == null) return;

			var files = await Avalonia.Controls.TopLevel
				.GetTopLevel(topLevel)!
				.StorageProvider
				.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Select HLA NoVR Mod zip file",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new FilePickerFileType("Zip files") { Patterns = new[] { "*.zip" } }
					}
				});

			if (files.Count == 0) return;

			string zipPath = files[0].Path.LocalPath;
			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath))
			{
				InstallStatusMessage = "Game path not found. Please run Quick Setup.";
				return;
			}

			// Validate the zip
			var (version, branch) = _versioner.ReadZipInfo(zipPath);

			if (version == null)
			{
				InstallStatusMessage = "Invalid zip — could not find version.lua inside. Make sure you downloaded the correct file from GitHub.";
				return;
			}

			// Check branch matches settings
			LauncherSettings settings = _settingsService.LoadSettings();
			if (branch != null && branch != settings.ModBranch &&
				settings.ModBranch != "main")
			{
				InstallStatusMessage = $"Wrong zip! This zip is for the '{branch}' branch but you have '{settings.ModBranch}' selected in Settings. Please download the correct zip from GitHub.";
				return;
			}

			// Install
			IsBusy = true;
			IsInstalling = true;
			InstallProgress = 0;
			InstallStatusMessage = "Starting...";

			var progress = new Progress<double>(p =>
			{
				Dispatcher.UIThread.Post(() => InstallProgress = p);
			});

			await Task.Run(() => _versioner.InstallFromZip(
				zipPath,
				gamePath,
				settings.BackupLocation,
				progress,
				msg => Dispatcher.UIThread.Post(() => InstallStatusMessage = msg),
				err => Dispatcher.UIThread.Post(() =>
				{
					InstallStatusMessage = err;
					IsInstalling = false;
					IsBusy = false;
				})
			));

			IsInstalling = false;
			IsBusy = false;
			UpdateStatus();
		}

		[RelayCommand]
		private void QuickSetup() => RequestSetup?.Invoke();

		public void UpdateStatus()
		{
			string steamPath = _gameService.GetSteamInstallPath() ?? "";

			if (string.IsNullOrEmpty(steamPath))
			{
				Status = LauncherStatus.SteamNotFound;
				return;
			}

			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath) ||
				!_gameService.IsGameInstalled(gamePath))
			{
				Status = LauncherStatus.MissingFiles;
				return;
			}

			string? modVersion = _versioner.GetInstalledModVersion(gamePath);
			if (modVersion == null)
			{
				Status = LauncherStatus.ModNotInstalled;
				InstalledModVersion = "";
				return;
			}

			InstalledModVersion = $"Mod version: {modVersion}";
			Status = LauncherStatus.Ready;
		}
	}
}