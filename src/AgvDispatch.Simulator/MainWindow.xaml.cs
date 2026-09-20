using System.ComponentModel;
using AgvDispatch.Simulator.Services;
using AgvDispatch.Simulator.ViewModels;
using HandyControl.Controls;

namespace AgvDispatch.Simulator;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// Inherits HandyControl.Controls.Window for borderless styling with NonClientAreaContent.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SimMqttClient _mqttClient;
    private readonly MovementEngine _movementEngine;
    private readonly BatterySim _batterySim;
    private readonly SimulatorCoordinator _coordinator;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var options = new SimMqttOptions
        {
            Host = "[IP]",
            Port = 1883,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001"
        };

        _mqttClient = new SimMqttClient(options);
        _movementEngine = new MovementEngine();
        _batterySim = new BatterySim();
        _coordinator = new SimulatorCoordinator(_mqttClient, _movementEngine, _batterySim);
        _viewModel = new MainViewModel(_coordinator);

        DataContext = _viewModel;

        _coordinator.Start();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Graceful shutdown: disconnect MQTT with OFFLINE per §11
        try
        {
            await _coordinator.DisposeAsync();
        }
        catch
        {
            // ignore during shutdown
        }
    }
}
