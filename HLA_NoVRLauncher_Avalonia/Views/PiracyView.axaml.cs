using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using HLA_NoVRLauncher_Avalonia.Services;
using HLA_NoVRLauncher_Avalonia.ViewModels;
using System;
using System.Diagnostics;

namespace HLA_NoVRLauncher_Avalonia.Views
{
    public partial class PiracyView : UserControl
    {
        public PiracyView()
        {
            InitializeComponent();

            OpenSteamButton.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "steam://store/546560",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://store.steampowered.com/app/546560/",
                        UseShellExecute = true
                    });
                }
            };

			RerunCheckButton.Click += (_, _) =>
			{
				OwnershipChecker.RemoveMarker();
				var result = OwnershipChecker.Check();

				if (result == OwnershipResult.Owned || result == OwnershipResult.Inconclusive)
				{
					if (Avalonia.Application.Current?.ApplicationLifetime
							is IClassicDesktopStyleApplicationLifetime desktop &&
						desktop.MainWindow?.DataContext is MainWindowViewModel vm)
					{
						vm.NavigateToLauncher();

						// Re-activate so Avalonia re-registers pointer events on the sidebar
						desktop.MainWindow.Activate();
					}
				}
				else
				{
					DataContext = new PiracyViewModel(result);
				}
			};
		}
    }
}
