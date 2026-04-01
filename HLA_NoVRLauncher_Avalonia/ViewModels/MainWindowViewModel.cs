using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public class MainWindowViewModel : ViewModelBase
	{
		private readonly GameService _gameService;
		private readonly LaunchService _launchService;
		private readonly SettingsService _settingsService;

		private LauncherSettings _settings;
		private string _statusMessage = "Checking game status...";
		private bool _isBusy;
		private bool _isSidebarOpen = true;

		public MainWindowViewModel()
		{
			_gameService = new GameService();
			_launchService = new LaunchService();
			_settingsService = new SettingsService();

			_settings = _settingsService.LoadSettings();

			LaunchGameCommand = MiniCommand.CreateFromTask(LaunchGameAsync);
			ToggleSidebarCommand = MiniCommand.CreateFromTask(ToggleSidebarAsync);

			UpdateStatus();
		}

		public LauncherSettings Settings
		{
			get => _settings;
			set
			{
				_settings = value;
				OnPropertyChanged();
			}
		}

		public string StatusMessage
		{
			get => _statusMessage;
			set
			{
				_statusMessage = value;
				OnPropertyChanged();
			}
		}

		public bool IsBusy
		{
			get => _isBusy;
			set
			{
				_isBusy = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(CanLaunch));
			}
		}

		public bool IsSidebarOpen
		{
			get => _isSidebarOpen;
			set
			{
				_isSidebarOpen = value;
				OnPropertyChanged();
			}
		}

		public bool CanLaunch => !IsBusy;

		public ICommand LaunchGameCommand { get; }
		public ICommand ToggleSidebarCommand { get; }

		public void UpdateStatus()
		{
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			string gamePath = string.IsNullOrEmpty(Settings.GamePath)
				? _gameService.GetDefaultGamePath(steamPath)
				: Settings.GamePath;

			if (!_gameService.IsGameInstalled(gamePath))
			{
				StatusMessage = "Half-Life: Alyx not found.";
			}
			else
			{
				StatusMessage = "Ready to Launch (NoVR)";
			}
		}

		public async Task ToggleSidebarAsync()
		{
			IsSidebarOpen = !IsSidebarOpen;
			await Task.CompletedTask;
		}

		public async Task LaunchGameAsync()
		{
			IsBusy = true;
			StatusMessage = "Game is running...";

			_settingsService.SaveSettings(Settings);

			_launchService.LaunchGame(Settings.GamePath, Settings.CustomLaunchArgs, () =>
			{
				IsBusy = false;
				UpdateStatus();
			});

			await Task.CompletedTask;
		}
	}

	public class MiniCommand : ICommand
	{
		private readonly Func<Task> _action;
		public MiniCommand(Func<Task> action) => _action = action;
		public static MiniCommand CreateFromTask(Func<Task> action) => new MiniCommand(action);
		public bool CanExecute(object? parameter) => true;
		public async void Execute(object? parameter) => await _action();
		public event EventHandler? CanExecuteChanged;
	}
}