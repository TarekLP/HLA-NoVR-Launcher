using System;
using System.Collections.Generic;
using System.Text;

namespace HLA_NoVRLauncher_Avalonia.Models
{
	public enum LauncherStatus
	{
		Ready,
		GameRunning,
		MissingFiles,
		SteamNotFound,
		CheckingStatus,
		ModNotInstalled
	}
}
