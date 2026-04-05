using Avalonia.Controls;
using HLA_NoVRLauncher_Avalonia.ViewModels;

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
					vm.PropertyChanged += OnViewModelPropertyChanged;
			};
		}



		private void OnRequestCloseLauncher()
		{
			WindowState = WindowState.Minimized;
		}
		private void OnViewModelPropertyChanged(object? sender,
			System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
			{
				if (DataContext is MainWindowViewModel vm &&
					vm.CurrentPage is HomeViewModel homeVm)
				{
					homeVm.RequestCloseLauncher += OnRequestCloseLauncher;
					homeVm.RequestSetup += OnRequestSetup;
				}
			}
		}

		private void OnRequestSetup()
		{
			if (DataContext is MainWindowViewModel vm)
				vm.RunSetupCommand.Execute(null);
		}
	}
}