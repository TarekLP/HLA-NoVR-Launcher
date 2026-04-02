using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class MainWindowViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService = new();
		public double SidebarWidth => IsSidebarOpen ? 220 : 48;

		[ObservableProperty]
		private bool _isSidebarOpen = true;

		[ObservableProperty]
		private object _currentPage;

		[ObservableProperty]
		private LauncherSettings _settings;

		public MainWindowViewModel()
		{
			_settings = _settingsService.LoadSettings();
			_currentPage = new HomeViewModel();
		}

		

		partial void OnIsSidebarOpenChanged(bool value)
		{
			OnPropertyChanged(nameof(SidebarWidth));
		}

		[RelayCommand]
		private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

		[RelayCommand]
		private void NavigateMain() => CurrentPage = new HomeViewModel();

		[RelayCommand]
		private void NavigateSettings() => CurrentPage = new SettingsViewModel(Settings);
	}
}