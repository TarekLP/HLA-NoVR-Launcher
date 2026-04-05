using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class SetupViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService = new();
		private readonly GameService _gameService = new();
		public bool IsStep0 => CurrentStep == 0;
		public bool IsStep1 => CurrentStep == 1;

		[ObservableProperty]
		private int _currentStep = 0;

		[ObservableProperty]
		private string _gamePath = "";

		[ObservableProperty]
		private string _selectedBranch = "main";

		[ObservableProperty]
		private bool _gamePathValid = false;

		[ObservableProperty]
		private string _gamePathMessage = "";

		[ObservableProperty]
		private string _gamePathMessageColor = "#E53935";

		public List<string> AvailableBranches { get; } = new()
		{
			"main",
			"mods",
			"steam_deck"
		};

		// Fired when setup is complete
		public event Action? SetupComplete;

		public SetupViewModel()
		{
			// Try to auto-detect game path
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			if (!string.IsNullOrEmpty(steamPath))
			{
				string detected = _gameService.GetDefaultGamePath(steamPath);
				if (_gameService.IsGameInstalled(detected))
				{
					GamePath = detected;
					ValidateGamePath(detected);
				}
			}
		}

		partial void OnGamePathChanged(string value)
		{
			ValidateGamePath(value);
		}

		private void ValidateGamePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				GamePathValid = false;
				GamePathMessage = "";
				GamePathMessageColor = "#E53935";
				return;
			}

			if (_gameService.IsGameInstalled(path))
			{
				GamePathValid = true;
				GamePathMessage = "✓ Half-Life: Alyx found!";
				GamePathMessageColor = "#1DB954";
			}
			else
			{
				GamePathValid = false;
				GamePathMessage = "✗ Half-Life: Alyx not found at this path.";
				GamePathMessageColor = "#E53935";
			}
		}

		[RelayCommand]
		private async Task BrowseAsync()
		{
			var topLevel = TopLevel.GetTopLevel(
				Avalonia.Application.Current?.ApplicationLifetime is
				IClassicDesktopStyleApplicationLifetime desktop
					? desktop.MainWindow
					: null);

			if (topLevel == null) return;

			var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
				new FolderPickerOpenOptions
				{
					Title = "Select your Half-Life: Alyx folder",
					AllowMultiple = false
				});

			if (folders.Count > 0)
				GamePath = folders[0].Path.LocalPath;
		}

		[RelayCommand]
		private void Next()
		{
			if (CurrentStep < 1)
				CurrentStep++;
		}

		[RelayCommand]
		private void Back()
		{
			if (CurrentStep > 0)
				CurrentStep--;
		}

		[RelayCommand]
		private void Finish()
		{
			var settings = _settingsService.LoadSettings();
			settings.GamePath = GamePath;
			settings.ModBranch = SelectedBranch;
			settings.FirstRun = false;
			_settingsService.SaveSettings(settings);
			SetupComplete?.Invoke();
		}
		partial void OnCurrentStepChanged(int value)
		{
			OnPropertyChanged(nameof(IsStep0));
			OnPropertyChanged(nameof(IsStep1));
		}
	}
}