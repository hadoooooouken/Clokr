using System.ComponentModel;
using System.Windows;
using Clokr.ViewModels;

namespace Clokr;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public bool IsExiting { get; set; }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (IsExiting)
        {
            return;
        }

        if (_viewModel?.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // If not minimizing to tray, exit the entire application to prevent "zombie" process
        IsExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel?.MinimizeToTray == true)
        {
            Hide();
        }
    }
}