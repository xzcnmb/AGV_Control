using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgvDispatch.MasterControl.Services;

namespace AgvDispatch.MasterControl.ViewModels;

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    public EmbeddedBroker Broker { get; }
    public MqttClientService MqttClient { get; }
    public AgvRegistry Registry { get; }
    public OrderService OrderService { get; }
    public TaskRepository Repository { get; }

    public AgvListViewModel AgvList { get; }
    public OrderEditorViewModel OrderEditor { get; }
    public MapViewModel Map { get; }
    public MqttMonitorViewModel MqttMonitor { get; }
    public TaskHistoryViewModel TaskHistory { get; }

    [ObservableProperty]
    private bool _isBrokerRunning;

    [ObservableProperty]
    private bool _isClientConnected;

    [ObservableProperty]
    private string _brokerStatusText = "Broker: 初始化中...";

    [ObservableProperty]
    private string _clientStatusText = "客户端: 初始化中...";

    [ObservableProperty]
    private string _systemStatusMessage = "系统就绪。";

    public MainViewModel(int brokerPort = 1883, string? dbPath = null)
    {
        Repository = new TaskRepository(dbPath);
        Broker = new EmbeddedBroker(brokerPort);
        MqttClient = new MqttClientService("[IP]", brokerPort, "MasterControl_WPF");
        Registry = new AgvRegistry(Repository);
        OrderService = new OrderService(MqttClient, Registry, Repository);

        // Wire MQTT client message events to AgvRegistry
        MqttClient.ConnectionReceived += Registry.OnConnectionReceived;
        MqttClient.StateReceived += Registry.OnStateReceived;
        MqttClient.FactsheetReceived += Registry.OnFactsheetReceived;
        MqttClient.VisualizationReceived += Registry.OnVisualizationReceived;

        MqttClient.ConnectionStatusChanged += connected =>
        {
            IsClientConnected = connected;
            ClientStatusText = connected ? "客户端: 已连接 (uagv/v2/#)" : "客户端: 已断开";
        };

        OrderService.NotificationRaised += (level, msg) =>
        {
            SystemStatusMessage = $"[{level}] {msg}";
        };

        // Initialize sub-viewmodels
        AgvList = new AgvListViewModel(Registry, OrderService);
        OrderEditor = new OrderEditorViewModel(OrderService, Registry);
        Map = new MapViewModel(Registry, OrderEditor);
        MqttMonitor = new MqttMonitorViewModel(Broker, MqttClient);
        TaskHistory = new TaskHistoryViewModel(Repository);

        // Synchronize selected AGV across views
        AgvList.SelectedAgvChanged += agv =>
        {
            Map.SelectedAgv = agv;
            OrderEditor.SelectedAgv = agv;
        };
    }

    public async Task StartAsync()
    {
        try
        {
            SystemStatusMessage = "正在启动内置 MQTT Broker (监听端口 1883)...";
            await Broker.StartAsync();
            IsBrokerRunning = true;
            BrokerStatusText = "Broker: 监听中 [::]:1883";

            SystemStatusMessage = "正在连接主控 MQTT 客户端...";
            await MqttClient.ConnectAsync();
            IsClientConnected = MqttClient.IsConnected;
            ClientStatusText = IsClientConnected ? "客户端: 已连接 (uagv/v2/#)" : "客户端: 连接中...";

            SystemStatusMessage = "系统就绪，Broker 与客户端已正常运行。";
        }
        catch (Exception ex)
        {
            SystemStatusMessage = $"启动异常: {ex.Message}";
            BrokerStatusText = $"Broker 错误: {ex.Message}";
        }
    }

    public async Task StopAsync()
    {
        try
        {
            SystemStatusMessage = "系统正在关闭...";
            await MqttClient.DisconnectAsync();
            await Broker.StopAsync();
            IsBrokerRunning = false;
            IsClientConnected = false;
            BrokerStatusText = "Broker: 已停止";
            ClientStatusText = "客户端: 已断开";
        }
        catch (Exception ex)
        {
            SystemStatusMessage = $"关闭异常: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        MqttMonitor.Dispose();
        OrderService.Dispose();
        Registry.Dispose();
        await MqttClient.DisposeAsync();
        await Broker.DisposeAsync();
        Repository.Dispose();
    }
}
