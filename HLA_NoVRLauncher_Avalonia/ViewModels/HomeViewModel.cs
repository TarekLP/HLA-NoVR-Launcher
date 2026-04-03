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

		public string StatusMessage => _status switch
		{
			LauncherStatus.Ready => "Ready to Launch (NoVR)",
			LauncherStatus.GameRunning => "Game is running...",
			LauncherStatus.MissingFiles => "Half-Life: Alyx not found.",
			LauncherStatus.SteamNotFound => "Steam installation not found.",
			LauncherStatus.CheckingStatus => "Checking game status...",
			_ => "Unknown status."
		};

		public string InstallButtonText => _updateAvailable
			? "Update Mod"
			: "Check for Updates";

		public bool CanLaunch => !IsBusy;
		public bool CanInstall => !IsBusy;

		public HomeViewModel()
		{
			var settings = _settingsService.LoadSettings();
			UpdateStatus();
			_ = CheckForUpdatesAsync();
		}

		partial void OnStatusChanged(LauncherStatus value)
		{
			OnPropertyChanged(nameof(StatusMessage));
		}

		partial void OnUpdateAvailableChanged(bool value)
		{
			OnPropertyChanged(nameof(InstallButtonText));
		}

		[RelayCommand(CanExecute = nameof(CanLaunch))]
		private async Task LaunchGameAsync()
		{
			IsBusy = true;
			Status = LauncherStatus.GameRunning;
			LauncherSettings settings = _settingsService.LoadSettings();
			_settingsService.SaveSettings(settings);

			_launchService.LaunchGame(settings.CustomLaunchArgs, () =>
			{
				Dispatcher.UIThread.Post(() =>
				{
					IsBusy = false;
					UpdateStatus();
				});
			});

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
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			string gamePath = string.IsNullOrEmpty(settings.GamePath)
				? _gameService.GetDefaultGamePath(steamPath)
				: settings.GamePath;

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
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			string gamePath = string.IsNullOrEmpty(settings.GamePath)
				? _gameService.GetDefaultGamePath(steamPath)
				: settings.GamePath;

			string? installedVersion = _versioner.GetInstalledModVersion(gamePath);
			if (installedVersion == null)
			{
				UpdateAvailable = false;
				return;
			}

			UpdateAvailable = await _versioner.IsModUpdateAvailableAsync(installedVersion, settings.ModBranch);
		}

		public void UpdateStatus()
		{
			string steamPath = _gameService.GetSteamInstallPath() ?? "";

			if (string.IsNullOrEmpty(steamPath))
			{
				Status = LauncherStatus.SteamNotFound;
				return;
			}

			LauncherSettings settings = _settingsService.LoadSettings();
			string gamePath = string.IsNullOrEmpty(settings.GamePath)
				? _gameService.GetDefaultGamePath(steamPath)
				: settings.GamePath;

			Status = _gameService.IsGameInstalled(gamePath)
				? LauncherStatus.Ready
				: LauncherStatus.MissingFiles;
		}
	}
}