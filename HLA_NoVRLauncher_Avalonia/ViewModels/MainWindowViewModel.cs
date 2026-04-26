using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using HLA_NoVRLauncher_Avalonia.Views;
using System;
using System.Reflection;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService = new();

		[ObservableProperty]
		private bool _isSidebarOpen = true;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(SidebarWidth))]
		private bool _isSidebarVisible = true;

		[ObservableProperty]
		private object _currentPage;

		[ObservableProperty]
		private LauncherSettings _settings;

		public static string LauncherVersion =>
			"v" + (Assembly.GetExecutingAssembly()
						   .GetName().Version?.ToString(3) ?? "1.0.0");

		public double SidebarWidth => IsSidebarOpen ? 220 : 60;

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
		}

		partial void OnIsSidebarOpenChanged(bool value)
		{
			OnPropertyChanged(nameof(SidebarWidth));
		}

		partial void OnCurrentPageChanged(object value)
		{
			// Hide the entire sidebar on the piracy screen so users
			// cannot navigate away from it using the sidebar buttons.
			IsSidebarVisible = value is not PiracyView;
		}

		[RelayCommand]
		private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

		[RelayCommand]
		public void NavigateMain()
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

		[RelayCommand]
		private void NavigateLog() => CurrentPage = new CrashLogViewModel();
	}
}
