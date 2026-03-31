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

		public MainWindowViewModel()
		{
			_gameService = new GameService();
			_launchService = new LaunchService();
			_settingsService = new SettingsService();

			// Load saved user data
			_settings = _settingsService.LoadSettings();

			// Initialize the Command for the Play button
			LaunchGameCommand = MiniCommand.CreateFromTask(LaunchGameAsync);

			UpdateStatus();
		}

		// The UI binds to this property for paths and arguments
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

		public bool CanLaunch => !IsBusy;

		// This is the property the "Play" button looks for
		public ICommand LaunchGameCommand { get; }

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

		public async Task LaunchGameAsync()
		{
			IsBusy = true;
			StatusMessage = "Game is running...";

			// Save settings automatically before launching
			_settingsService.SaveSettings(Settings);

			_launchService.LaunchGame(Settings.GamePath, Settings.CustomLaunchArgs, () =>
			{
				IsBusy = false;
				UpdateStatus();
			});

			await Task.CompletedTask;
		}
	}

	// A simple helper class to handle the button click without needing extra libraries
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
