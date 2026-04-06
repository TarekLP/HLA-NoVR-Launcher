using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
        private readonly GameService _gameService;

        [ObservableProperty]
        private LauncherSettings _settings;

        [ObservableProperty]
        private string _selectedBranch;

		[ObservableProperty]
		private string _detectedBranch = "Not detected";

		[ObservableProperty]
        private string _selectedResolution;

        [ObservableProperty]
        private string _gamePath = "";

        public List<string> AvailableBranches { get; } = new()
        {
            "main",
            "mods",
            "steam_deck"
        };

        public List<string> CommonResolutions { get; } = new()
        {
            "1280x720",
            "1920x1080",
            "2560x1440",
            "3840x2160",
            "Custom"
        };

		public SettingsViewModel(LauncherSettings currentSettings)
		{
			_settingsService = new SettingsService();
			_gameService = new GameService();
			_settings = currentSettings;

			string res = $"{currentSettings.FullscreenWidth}x{currentSettings.FullscreenHeight}";
			_selectedResolution = CommonResolutions.Contains(res) ? res : "Custom";

			if (!string.IsNullOrEmpty(currentSettings.GamePath))
				_gamePath = currentSettings.GamePath;
			else
			{
				string steamPath = _gameService.GetSteamInstallPath() ?? "";
				_gamePath = !string.IsNullOrEmpty(steamPath)
					? _gameService.GetDefaultGamePath(steamPath)
					: "";
			}

			// Detect installed branch
			var versioner = new LauncherVersioner();
			string? branch = versioner.GetInstalledBranch(_gamePath);
			_detectedBranch = branch switch
			{
				"main" => "Stable Branch - No Mods",
				"mods" => "Stable Branch - Community Mods",
				"steam_deck" => "Steam Deck Branch - Linux / Steam Deck Users",
				null => "Not detected",
				_ => branch
			};

			// Auto-save on any change
			PropertyChanged += (_, _) => Save();
			Settings.PropertyChanged += (_, _) => Save();
		}

		private void Save()
        {
            Settings.ModBranch = SelectedBranch;
            Settings.GamePath = GamePath;
            _settingsService.SaveSettings(Settings);
        }

        partial void OnSelectedBranchChanged(string value)
        {
            Settings.ModBranch = value;
        }

        partial void OnSelectedResolutionChanged(string value)
        {
            if (value == "Custom") return;
            var parts = value.Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int w) &&
                int.TryParse(parts[1], out int h))
            {
                Settings.FullscreenWidth = w;
                Settings.FullscreenHeight = h;
            }
        }

        partial void OnGamePathChanged(string value)
        {
            Settings.GamePath = value;
        }

        [RelayCommand]
        private async Task BrowseGamePathAsync()
        {
            var topLevel = TopLevel.GetTopLevel(
                Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null);

            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select Half-Life: Alyx folder",
                    AllowMultiple = false
                });

            if (folders.Count > 0)
                GamePath = folders[0].Path.LocalPath;
        }

		[RelayCommand]
		private async Task UninstallModAsync()
		{
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			string gamePath = string.IsNullOrEmpty(_settings.GamePath)
				? _gameService.GetDefaultGamePath(steamPath)
				: _settings.GamePath;

			await Task.Run(() => new LauncherVersioner().UninstallMod(
				gamePath,
				onStatus: msg => System.Diagnostics.Debug.WriteLine(msg),
				onError: err => System.Diagnostics.Debug.WriteLine(err)
			));
		}
	}
}