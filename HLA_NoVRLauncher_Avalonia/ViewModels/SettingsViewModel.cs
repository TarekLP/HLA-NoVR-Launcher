using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class SettingsViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService;

		[ObservableProperty]
		private LauncherSettings _settings;

		public SettingsViewModel(LauncherSettings currentSettings)
		{
			_settingsService = new SettingsService();
			_settings = currentSettings;
		}

		[RelayCommand]
		private async Task SaveSettingsAsync()
		{
			_settingsService.SaveSettings(Settings);
			await Task.CompletedTask;
		}
	}
}