using Avalonia.Controls;

namespace HLA_NoVRLauncher_Avalonia.Views
{
	/// <summary>
	/// The transparent overlay window that sits on top of the HLA game window.
	/// This code-behind is intentionally minimal — all behaviour lives in
	/// OverlayService and (eventually) GameMenuViewModel.
	/// </summary>
	public partial class GameMenuWindow : Window
	{
		public GameMenuWindow()
		{
			InitializeComponent();
		}
	}
}
