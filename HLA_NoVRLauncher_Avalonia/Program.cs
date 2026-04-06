using Avalonia;
using System;

namespace HLA_NoVRLauncher_Avalonia
{
	internal sealed class Program
	{
		// Initialization code. Don't use any Avalonia, third-party APIs or any
		// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
		// yet and stuff might break

		[STAThread]
		public static void Main(string[] args) => BuildAvaloniaApp()
			.StartWithClassicDesktopLifetime(args);

		public static AppBuilder BuildAvaloniaApp()
			=> AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.WithInterFont()
				.LogToTrace()
				#if LINUX
				.With(new X11PlatformOptions
				{
					UseDBusMenu = false,
					UseDBusFilePicker = false
				})
				#endif
				;
	}
}