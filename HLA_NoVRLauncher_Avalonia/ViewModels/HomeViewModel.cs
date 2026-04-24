using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class HomeViewModel : ViewModelBase
	{
		// -----------------------------------------------------------------------
		// Services
		// -----------------------------------------------------------------------

		private readonly GameService            _gameService      = new();
		private readonly LaunchService          _launchService    = new();
		private readonly SettingsService        _settingsService  = new();
		private readonly LauncherVersioner      _versioner        = new();
		private readonly LauncherHelperService  _helperService    = new();

		// Kept alive for the duration of the game session; disposed on game exit.
		private OverlayService? _overlayService;

		// -----------------------------------------------------------------------
		// Observable properties (unchanged from before)
		// -----------------------------------------------------------------------

		[ObservableProperty]
		[NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
		[NotifyCanExecuteChangedFor(nameof(InstallModCommand))]
		private bool _isBusy;

		[ObservableProperty]
		private LauncherStatus _status = LauncherStatus.CheckingStatus;

		[ObservableProperty]
		private double _installProgress = 0;

		[ObservableProperty]
		private bool _isInstalling;

		[ObservableProperty]
		private string _installStatusMessage = "";

		[ObservableProperty]
		private string _installedModVersion = "";

		// -----------------------------------------------------------------------
		// Computed properties
		// -----------------------------------------------------------------------

		public string StatusMessage => _status switch
		{
			LauncherStatus.Ready          => "Ready to Launch (NoVR)",
			LauncherStatus.GameRunning    => "Game is running...",
			LauncherStatus.MissingFiles   => "Half-Life: Alyx not found.",
			LauncherStatus.SteamNotFound  => "Steam installation not found.",
			LauncherStatus.CheckingStatus => "Checking game status...",
			LauncherStatus.ModNotInstalled => "Mod not installed — click Install!",
			_ => "Unknown status."
		};

		public string InstallButtonText => _status == LauncherStatus.ModNotInstalled
			? "Install Mod"
			: "Reinstall Mod";

		public bool CanLaunch  => !IsBusy;
		public bool CanInstall => !IsBusy;

		// -----------------------------------------------------------------------
		// Events — wired up by MainWindow.axaml.cs
		// -----------------------------------------------------------------------

		/// <summary>Minimise the launcher window when the game starts.</summary>
		public event Action? RequestCloseLauncher;

		/// <summary>Restore the launcher window when the game exits.</summary>
		public event Action? RequestShowLauncher;

		/// <summary>Navigate to the setup screen.</summary>
		public event Action? RequestSetup;

		/// <summary>
		/// Factory that creates the overlay window.
		/// Set by MainWindow.axaml.cs so that window construction stays in the
		/// View layer (HomeViewModel never references GameMenuWindow directly).
		///
		/// OverlayService calls this on the UI thread, so it is safe to
		/// construct an Avalonia Window inside the factory.
		/// </summary>
		public Func<Window>? OverlayWindowFactory { get; set; }

		// -----------------------------------------------------------------------
		// Constructor
		// -----------------------------------------------------------------------

		public HomeViewModel()
		{
			UpdateStatus();
		}

		// -----------------------------------------------------------------------
		// Property change callbacks
		// -----------------------------------------------------------------------

		partial void OnStatusChanged(LauncherStatus value)
		{
			OnPropertyChanged(nameof(StatusMessage));
			OnPropertyChanged(nameof(InstallButtonText));
		}

		// -----------------------------------------------------------------------
		// Helpers
		// -----------------------------------------------------------------------

		private string ResolveGamePath()
		{
			LauncherSettings settings = _settingsService.LoadSettings();
			if (!string.IsNullOrEmpty(settings.GamePath))
				return settings.GamePath;

			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			if (string.IsNullOrEmpty(steamPath))
				return "";

			string detectedPath = _gameService.GetDefaultGamePath(steamPath);
			settings.GamePath   = detectedPath;
			_settingsService.SaveSettings(settings);
			return detectedPath;
		}

		// -----------------------------------------------------------------------
		// Launch command
		// -----------------------------------------------------------------------

		[RelayCommand(CanExecute = nameof(CanLaunch))]
		private async Task LaunchGameAsync()
		{
			IsBusy = true;
			Status = LauncherStatus.GameRunning;

			LauncherSettings settings = _settingsService.LoadSettings();
			_settingsService.SaveSettings(settings);

			string gamePath = ResolveGamePath();

			// Minimise the launcher before the game window appears
			if (settings.CloseLauncherOnStart)
				RequestCloseLauncher?.Invoke();

			// ---- Launch the game ----
			// The callback fires when the hlvr.exe process exits.
			_launchService.LaunchGame(settings.CustomLaunchArgs, () =>
			{
				// Dispose the overlay first, then restore the launcher on the UI thread
				_overlayService?.Dispose();
				_overlayService = null;

				Dispatcher.UIThread.Post(() =>
				{
					RequestShowLauncher?.Invoke();
					IsBusy = false;
					UpdateStatus();
				});
			}, settings);

			// ---- Start the overlay (unless the game is using its own default menu) ----
			// OverlayService.InitializeAsync blocks internally until hlvr.exe appears,
			// so we fire it as a background task and let it catch up to the game process.
			if (!settings.DefaultMenu && !string.IsNullOrEmpty(gamePath))
			{
				Func<Window>? factory = OverlayWindowFactory;

				if (factory != null)
				{
					_overlayService = new OverlayService(_helperService);

					// Log state transitions — useful while building the real menu
					_overlayService.StateChanged += state =>
						Console.WriteLine($"[Overlay] → {state}");

					// Log if the geometry loop detects the game window is gone
					_overlayService.GameExited += () =>
						Console.WriteLine("[Overlay] Game window lost.");

					// Log achievement requests (handler goes here in a later step)
					_overlayService.AchievementReceived += id =>
						Console.WriteLine($"[Overlay] Achievement requested: {id}");

					// Run on a thread-pool thread — InitializeAsync is fully async
					// and dispatches window creation back to the UI thread itself.
					_ = Task.Run(async () =>
					{
						try
						{
							Console.WriteLine("[Overlay] InitializeAsync starting...");
							await _overlayService.InitializeAsync(gamePath, factory);
						}
						catch (OperationCanceledException)
						{
							// Game exited before overlay could initialise — normal.
						}
						catch (Exception ex)
						{
							Console.WriteLine($"[Overlay] Init failed: {ex.Message}");
						}
					});
				}
			}

			await Task.CompletedTask;
		}

		// -----------------------------------------------------------------------
		// Install command (unchanged)
		// -----------------------------------------------------------------------

		[RelayCommand(CanExecute = nameof(CanInstall))]
		private async Task InstallModAsync()
		{
			var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
				IClassicDesktopStyleApplicationLifetime desktop
					? desktop.MainWindow
					: null;

			if (topLevel == null) return;

			var files = await Avalonia.Controls.TopLevel
				.GetTopLevel(topLevel)!
				.StorageProvider
				.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Select HLA NoVR Mod zip file",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new FilePickerFileType("Zip files") { Patterns = new[] { "*.zip" } }
					}
				});

			if (files.Count == 0) return;

			string zipPath  = files[0].Path.LocalPath;
			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath))
			{
				InstallStatusMessage = "Game path not found. Please run Quick Setup.";
				return;
			}

			var (version, branch) = _versioner.ReadZipInfo(zipPath);

			if (version == null)
			{
				InstallStatusMessage = "Invalid zip — could not find version.lua inside. Make sure you downloaded the correct file from GitHub.";
				return;
			}

			LauncherSettings settings = _settingsService.LoadSettings();
			if (branch != null && branch != settings.ModBranch && settings.ModBranch != "main")
			{
				InstallStatusMessage = $"Wrong zip! This zip is for the '{branch}' branch but you have '{settings.ModBranch}' selected in Settings.";
				return;
			}

			IsBusy            = true;
			IsInstalling      = true;
			InstallProgress   = 0;
			InstallStatusMessage = "Starting...";

			var progress = new Progress<double>(p =>
				Dispatcher.UIThread.Post(() => InstallProgress = p));

			await Task.Run(() => _versioner.InstallFromZip(
				zipPath,
				gamePath,
				settings.BackupLocation,
				progress,
				msg => Dispatcher.UIThread.Post(() => InstallStatusMessage = msg),
				err => Dispatcher.UIThread.Post(() =>
				{
					InstallStatusMessage = err;
					IsInstalling = false;
					IsBusy       = false;
				})
			));

			IsInstalling = false;
			IsBusy       = false;
			UpdateStatus();
		}

		// -----------------------------------------------------------------------
		// Other commands (unchanged)
		// -----------------------------------------------------------------------

		[RelayCommand]
		private void QuickSetup() => RequestSetup?.Invoke();

		// -----------------------------------------------------------------------
		// Status check
		// -----------------------------------------------------------------------

		public void UpdateStatus()
		{
			string steamPath = _gameService.GetSteamInstallPath() ?? "";

			if (string.IsNullOrEmpty(steamPath))
			{
				Status = LauncherStatus.SteamNotFound;
				return;
			}

			string gamePath = ResolveGamePath();

			if (string.IsNullOrEmpty(gamePath) || !_gameService.IsGameInstalled(gamePath))
			{
				Status = LauncherStatus.MissingFiles;
				return;
			}

			string? modVersion = _versioner.GetInstalledModVersion(gamePath);
			if (modVersion == null)
			{
				Status = LauncherStatus.ModNotInstalled;
				InstalledModVersion = "";
				return;
			}

			InstalledModVersion = $"Mod version: {modVersion}";
			Status = LauncherStatus.Ready;
		}
	}
}
