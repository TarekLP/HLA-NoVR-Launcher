using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using HLA_NoVRLauncher_Avalonia.Views;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService = new();
		private readonly LauncherVersioner _versioner = new();

		[ObservableProperty]
		private bool _isSidebarOpen = true;

		[ObservableProperty]
		private object _currentPage;

		[ObservableProperty]
		private LauncherSettings _settings;

		[ObservableProperty]
		private bool _launcherUpdateAvailable;

		[ObservableProperty]
		private string _latestLauncherVersion = "";

		[ObservableProperty]
		private bool _isCheckingForUpdates = true;

		public string LauncherVersion =>
			"v" + (Assembly.GetExecutingAssembly()
						   .GetName().Version?.ToString(3) ?? "1.0.0");

		public MainWindowViewModel()
		{
			_settings = _settingsService.LoadSettings();

			if (_settings.FirstRun)
			{
				var setup = new SetupViewModel();
				setup.SetupComplete += () =>
				{
					_settings = _settingsService.LoadSettings();
					CurrentPage = new HomeViewModel();
				};
				_currentPage = setup;
			}
			else
			{
				_currentPage = new HomeViewModel();
			}

			_ = CheckForLauncherUpdateAsync();
		}

		public double SidebarWidth => IsSidebarOpen ? 220 : 60;

		partial void OnIsSidebarOpenChanged(bool value)
		{
			OnPropertyChanged(nameof(SidebarWidth));
		}
		private async Task CheckForLauncherUpdateAsync()
		{
			IsCheckingForUpdates = true;

			string current = Assembly.GetExecutingAssembly()
									 .GetName().Version?.ToString(3) ?? "1.0.0";

			bool updateAvailable = await _versioner
				.IsLauncherUpdateAvailableAsync(current);

			if (updateAvailable)
			{
				string? latest = await _versioner.GetLatestLauncherVersionAsync();
				LatestLauncherVersion = latest ?? "";
				LauncherUpdateAvailable = true;
			}

			IsCheckingForUpdates = false;
		}

		[RelayCommand]
		private void OpenReleasesPage()
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "https://github.com/TarekLP/HLA-NoVR-Launcher/releases/latest",
				UseShellExecute = true
			});
		}

		[RelayCommand]
		private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

		[RelayCommand]
		private void NavigateMain()
		{
			if (_settings.FirstRun)
			{
				var setup = new SetupViewModel();
				setup.SetupComplete += () =>
				{
					_settings = _settingsService.LoadSettings();
					CurrentPage = new HomeViewModel();
				};
				CurrentPage = setup;
			}
			else
			{
				CurrentPage = new HomeViewModel();
			}
		}

		[RelayCommand]
		private void RunSetup()
		{
			var setup = new SetupViewModel();
			setup.SetupComplete += () =>
			{
				_settings = _settingsService.LoadSettings();
				CurrentPage = new HomeViewModel();
			};
			CurrentPage = setup;
		}

		[RelayCommand]
		private void NavigateSettings()
		{
			_settings = _settingsService.LoadSettings();
			CurrentPage = new SettingsViewModel(_settings);
		}
		
		public event Action? RequestSetup;

		[RelayCommand]
		private void QuickSetup()
		{
			RequestSetup?.Invoke();
		}
	}
}