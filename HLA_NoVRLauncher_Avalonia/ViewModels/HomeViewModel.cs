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
		private bool _updateAvailable;

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
			: _updateAvailable
				? "Update Mod"
				: "Check for Updates";

		public bool CanLaunch => !IsBusy;
		public bool CanInstall => !IsBusy;

		// Fired by MainWindow when it needs to close/minimize on launch
		public event Action? RequestCloseLauncher;

		public HomeViewModel()
		{
			UpdateStatus();
			_ = CheckForUpdatesAsync();
		}

		partial void OnStatusChanged(LauncherStatus value)
		{
			OnPropertyChanged(nameof(StatusMessage));
			OnPropertyChanged(nameof(InstallButtonText));
		}

		partial void OnUpdateAvailableChanged(bool value)
		{
			OnPropertyChanged(nameof(InstallButtonText));
		}
		private string ResolveGamePath()
		{
			LauncherSettings settings = _settingsService.LoadSettings();

			// Use manually saved path if set
			if (!string.IsNullOrEmpty(settings.GamePath))
				return settings.GamePath;

			// Auto-detect and immediately save so it persists
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			if (string.IsNullOrEmpty(steamPath))
				return "";

			string detectedPath = _gameService.GetDefaultGamePath(steamPath);

			// Save it so future calls don't need to re-detect
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
			IsBusy = true;
			IsInstalling = true;
			InstallProgress = 0;
			InstallStatusMessage = "Starting...";

			LauncherSettings settings = _settingsService.LoadSettings();
			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath))
			{
				InstallStatusMessage = "Could not find game path. Please set it in Settings.";
				IsInstalling = false;
				IsBusy = false;
				return;
			}

			var progress = new Progress<double>(p =>
			{
				Dispatcher.UIThread.Post(() => InstallProgress = p);
			});

			await _versioner.InstallOrUpdateModAsync(
				gamePath,
				branch: settings.ModBranch,
				onProgress: progress,
				onStatus: msg => Dispatcher.UIThread.Post(() =>
					InstallStatusMessage = msg),
				onError: err => Dispatcher.UIThread.Post(() =>
				{
					InstallStatusMessage = err;
					IsInstalling = false;
					IsBusy = false;
				})
			);

			await CheckForUpdatesAsync();

			IsInstalling = false;
			IsBusy = false;
			UpdateStatus();
		}

		public async Task CheckForUpdatesAsync()
		{
			LauncherSettings settings = _settingsService.LoadSettings();
			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath))
			{
				InstalledModVersion = "Mod not installed";
				UpdateAvailable = false;
				return;
			}

			string? installedVersion = _versioner.GetInstalledModVersion(gamePath);

			if (installedVersion == null)
			{
				InstalledModVersion = "Mod not installed";
				UpdateAvailable = false;
				return;
			}

			InstalledModVersion = $"Mod version: v{installedVersion}";
			UpdateAvailable = await _versioner.IsModUpdateAvailableAsync(
				installedVersion, settings.ModBranch);
		}

		public void UpdateStatus()
		{
			string steamPath = _gameService.GetSteamInstallPath() ?? "";

			if (string.IsNullOrEmpty(steamPath))
			{
				Status = LauncherStatus.SteamNotFound;
				return;
			}

			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath) || !_gameService.IsGameInstalled(gamePath))
			{
				Status = LauncherStatus.MissingFiles;
				return;
			}

			string? modVersion = _versioner.GetInstalledModVersion(gamePath);
			if (modVersion == null)
			{
				Status = LauncherStatus.ModNotInstalled;
				return;
			}

			Status = LauncherStatus.Ready;
		}
	}
}