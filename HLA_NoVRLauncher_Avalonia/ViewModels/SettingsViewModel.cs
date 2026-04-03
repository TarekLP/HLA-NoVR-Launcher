using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class SettingsViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService;
		private readonly GameService _gameService = new();

		[ObservableProperty]
		private LauncherSettings _settings;

		[ObservableProperty]
		private string _selectedBranch;

		public List<string> AvailableBranches { get; } = new()
		{
			"main",
			"mods",
			"steam_deck"
		};

		public SettingsViewModel(LauncherSettings currentSettings)
		{
			_settingsService = new SettingsService();
			_settings = currentSettings;
			_selectedBranch = currentSettings.ModBranch;
		}

		partial void OnSelectedBranchChanged(string value)
		{
			Settings.ModBranch = value;
		}

		[RelayCommand]
		private async Task SaveSettingsAsync()
		{
			Settings.ModBranch = SelectedBranch;
			_settingsService.SaveSettings(Settings);
			await Task.CompletedTask;
		}

		[RelayCommand]
		private void UninstallMod()
		{
			string verifyLink = _gameService.GetVerifyLink();
			Process.Start(new ProcessStartInfo
			{
				FileName = verifyLink,
				UseShellExecute = true
			});
		}
	}
}