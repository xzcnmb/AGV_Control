using System.Windows;
using HandyControl.Controls;
using AgvDispatch.MasterControl.ViewModels;

namespace AgvDispatch.MasterControl;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// Inherits HandyControl.Controls.Window for custom borderless chrome.
/// </summary>
public partial class MainWindow : HandyControl.Controls.Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // Start broker and client immediately on startup
        _ = _viewModel.StartAsync();

        Closing += async (s, e) =>
        {
            await _viewModel.StopAsync();
        };
    }

    private void LiveMapCanvas_MapClicked(double worldX, double worldY)
    {
        _viewModel.Map.OnMapClicked(worldX, worldY);
    }
}
