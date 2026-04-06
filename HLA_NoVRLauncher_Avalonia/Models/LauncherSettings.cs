using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace HLA_NoVRLauncher_Avalonia.Models
{
	public partial class LauncherSettings : ObservableObject
	{
		[ObservableProperty] private string _gamePath = string.Empty;
		[ObservableProperty] private string _customLaunchArgs = string.Empty;
		[ObservableProperty] private bool _autoLaunch = false;
		[ObservableProperty] private bool _closeLauncherOnStart = true;
		[ObservableProperty] private bool _isMuted = true;
		[ObservableProperty] private string _modBranch = "main";
		// HLVR Launch Options
		[ObservableProperty] private bool _windowed = false;
		[ObservableProperty] private bool _fullscreen = false;
		[ObservableProperty] private int _fullscreenWidth = 1920;
		[ObservableProperty] private int _fullscreenHeight = 1080;
		[ObservableProperty] private bool _defaultMenu = false;
		[ObservableProperty] private bool _vSync = false;
		[ObservableProperty] private bool _enableConsole = true;

		// Launcher Options
		[ObservableProperty] private bool _firstRun = true;
		[ObservableProperty] private string _backupLocation = "Launcher";
	}
}
