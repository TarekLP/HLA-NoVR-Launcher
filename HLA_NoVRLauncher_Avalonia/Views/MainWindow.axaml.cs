using Avalonia.Controls;
using HLA_NoVRLauncher_Avalonia.ViewModels;
using HLA_NoVRLauncher_Avalonia.Views;

namespace HLA_NoVRLauncher_Avalonia.Views
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();

			DataContextChanged += (_, _) =>
			{
				if (DataContext is MainWindowViewModel vm)
				{
					vm.PropertyChanged += OnViewModelPropertyChanged;
					if (vm.CurrentPage is HomeViewModel homeVm)
						WireHomeViewModel(homeVm);
				}
			};
		}

		private void WireHomeViewModel(HomeViewModel homeVm)
		{
			homeVm.RequestCloseLauncher += OnRequestCloseLauncher;
			homeVm.RequestShowLauncher  += OnRequestShowLauncher;
			homeVm.RequestSetup         += OnRequestSetup;

			// Give the ViewModel a factory that creates the overlay window.
			// Kept here so the View layer owns window construction and
			// HomeViewModel never needs to reference GameMenuWindow directly.
			homeVm.OverlayWindowFactory = () => new GameMenuWindow();
		}

		private void OnViewModelPropertyChanged(object? sender,
			System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
			{
				if (DataContext is MainWindowViewModel vm &&
					vm.CurrentPage is HomeViewModel homeVm)
				{
					WireHomeViewModel(homeVm);
				}
			}
		}

		private void OnRequestCloseLauncher()
		{
			WindowState = WindowState.Minimized;
		}

		private void OnRequestShowLauncher()
		{
			WindowState = WindowState.Normal;
			Activate(); // bring to front
		}

		private void OnRequestSetup()
		{
			if (DataContext is MainWindowViewModel vm)
				vm.RunSetupCommand.Execute(null);
		}
	}
}
