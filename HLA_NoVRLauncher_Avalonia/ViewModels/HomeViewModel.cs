using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class HomeViewModel : ViewModelBase
	{
		private readonly GameService _gameService = new();
		private readonly LaunchService _launchService = new();
		private readonly SettingsService _settingsService = new();

		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
		private bool _isBusy;

		[ObservableProperty]
		private LauncherStatus _status = LauncherStatus.CheckingStatus;

		public string StatusMessage => _status switch
		{
			LauncherStatus.Ready => "Ready to Launch (NoVR)",
			LauncherStatus.GameRunning => "Game is running...",
			LauncherStatus.MissingFiles => "Half-Life: Alyx not found.",
			LauncherStatus.SteamNotFound => "Steam installation not found.",
			LauncherStatus.CheckingStatus => "Checking game status...",
			_ => "Unknown status."
		};

		public HomeViewModel()
		{
			UpdateStatus();
		}

		public bool CanLaunch => !IsBusy;

		[RelayCommand(CanExecute = nameof(CanLaunch))]
		private async Task LaunchGameAsync()
		{
			IsBusy = true;
			Status = LauncherStatus.GameRunning;
			LauncherSettings settings = _settingsService.LoadSettings();
			_settingsService.SaveSettings(settings);
			_launchService.LaunchGame(settings.GamePath, settings.CustomLaunchArgs, () =>
			{
				Dispatcher.UIThread.Post(() =>
				{
					IsBusy = false;
					UpdateStatus();
				});
			});
			await Task.CompletedTask;
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

		partial void OnStatusChanged(LauncherStatus value)
		{
			OnPropertyChanged(nameof(StatusMessage));
		}
	}
}