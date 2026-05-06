using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HLA_NoVRLauncher_Avalonia.ViewModels;
using HLA_NoVRLauncher_Avalonia.Views;
using System.Runtime.InteropServices;

namespace HLA_NoVRLauncher_Avalonia
{

	public partial class App : Application
	{
		[DllImport("kernel32.dll")]
		private static extern bool AllocConsole();

		public override void Initialize()
		{
			#if DEBUG
				AllocConsole();
			#endif
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.MainWindow = new MainWindow
				{
					DataContext = new MainWindowViewModel(),
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}