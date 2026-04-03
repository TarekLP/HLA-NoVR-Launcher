using System;
using System.Collections.Generic;
using System.Text;

namespace HLA_NoVRLauncher_Avalonia.Models
{
	public class LauncherSettings
	{
		public string GamePath { get; set; } = string.Empty;
		public string CustomLaunchArgs { get; set; } = string.Empty;
		public bool AutoLaunch { get; set; } = false;
		public bool CloseLauncherOnStart { get; set; } = true;
		public bool IsMuted { get; set; } = false;
		public string ModBranch { get; set; } = "main";
	}
}
