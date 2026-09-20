using System.Collections.ObjectModel;
using System.Windows;
using AgvDispatch.Simulator.Models;
using AgvDispatch.Simulator.Services;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgvDispatch.Simulator.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SimulatorCoordinator _coordinator;
    private string _lastTrackedOrderId = string.Empty;
    private string _lastTrackedNodeId = string.Empty;
    private bool _lastTrackedDriving;

    [ObservableProperty]
    private string _host = "[IP]";

    [ObservableProperty]
    private int _port = 1883;

    [ObservableProperty]
    private string _manufacturer = "AgvSim";

    [ObservableProperty]
    private string _serialNumber = "AGV0001";

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.OFFLINE;

    [ObservableProperty]
    private string _connectionStateDisplay = "离线 (OFFLINE)";

    [ObservableProperty]
    private string _orderId = string.Empty;

    [ObservableProperty]
    private uint _orderUpdateId;

    [ObservableProperty]
    private string _lastNodeId = string.Empty;

    [ObservableProperty]
    private uint _lastNodeSequenceId;

    [ObservableProperty]
    private double _positionX;

    [ObservableProperty]
    private double _positionY;

    [ObservableProperty]
    private double _positionTheta;

    [ObservableProperty]
    private bool _driving;

    [ObservableProperty]
    private string _drivingDisplay = "静止";

    [ObservableProperty]
    private bool _paused;

    [ObservableProperty]
    private string _pausedDisplay = "正常运行";

    [ObservableProperty]
    private bool _charging;

    [ObservableProperty]
    private string _chargingDisplay = "未充电";

    [ObservableProperty]
    private double _batteryCharge = 100.0;

    [ObservableProperty]
    private OperatingMode _operatingMode = OperatingMode.AUTOMATIC;

    [ObservableProperty]
    private string _operatingModeDisplay = "自动 (AUTOMATIC)";

    [ObservableProperty]
    private int _nodeStatesCount;

    [ObservableProperty]
    private int _edgeStatesCount;

    [ObservableProperty]
    private bool _hasFatalError;

    public ObservableCollection<ActionState> ActionStates { get; } = new();
    public ObservableCollection<Error> Errors { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<string> ProtocolLogs { get; } = new();
    public ObservableCollection<SimWaypointItem> Waypoints { get; } = new();
    public ObservableCollection<Point> TrailPoints { get; } = new();

    public MainViewModel(SimulatorCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        _coordinator.StateUpdated += OnCoordinatorStateUpdated;
        _coordinator.LogMessage += OnCoordinatorLogMessage;
        _coordinator.MovementEngine.PositionChanged += OnPositionChanged;
        _coordinator.MqttClient.ConnectionStateChanged += OnMqttConnectionStateChanged;

        _host = _coordinator.MqttClient.Options.Host;
        _port = _coordinator.MqttClient.Options.Port;
        _manufacturer = _coordinator.MqttClient.Options.Manufacturer;
        _serialNumber = _coordinator.MqttClient.Options.SerialNumber;

        UpdateDisplays();
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        try
        {
            _coordinator.MqttClient.UpdateOptions(new SimMqttOptions
            {
                Host = Host.Trim(),
                Port = Port,
                Manufacturer = Manufacturer.Trim(),
                SerialNumber = SerialNumber.Trim()
            });

            AddLog($"正在发起连接: {Host}:{Port} (设备: {SerialNumber})...");
            await _coordinator.MqttClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            AddLog($"连接失败: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        try
        {
            AddLog("正在断开连接并发送离线消息(OFFLINE)...");
            await _coordinator.MqttClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AddLog($"断开连接出错: {ex.Message}");
        }
    }

    [RelayCommand]
    public void InjectWarning()
    {
        _coordinator.InjectWarning("manualWarning", "操作员手动注入警告信息 (WARNING)");
        AddLog("已注入警告 (WARNING)");
    }

    [RelayCommand]
    public void InjectFatal()
    {
        _coordinator.InjectFatal("manualFatal", "操作员手动注入致命错误 (FATAL)");
        AddLog("已注入致命错误 (FATAL)，小车停车并锁定订单！");
    }

    [RelayCommand]
    public void ClearErrors()
    {
        _coordinator.ClearErrors();
        AddLog("已清除全部错误与报警");
    }

    [RelayCommand]
    public void ClearTrail()
    {
        TrailPoints.Clear();
        TrailPoints.Add(new Point(PositionX, PositionY));
        AddLog("已清除历史轨迹");
    }

    [RelayCommand]
    public void ClearLogs()
    {
        Logs.Clear();
    }

    [RelayCommand]
    public void ClearProtocolLogs()
    {
        ProtocolLogs.Clear();
    }

    private void OnMqttConnectionStateChanged(ConnectionState state)
    {
        RunOnUi(() =>
        {
            ConnectionState = state;
            ConnectionStateDisplay = state switch
            {
                ConnectionState.ONLINE => "在线 (ONLINE)",
                ConnectionState.CONNECTIONBROKEN => "异常断开 (CONNECTIONBROKEN)",
                _ => "离线 (OFFLINE)"
            };
        });
    }

    private void OnPositionChanged(AgvPosition pos, Velocity vel)
    {
        RunOnUi(() =>
        {
            PositionX = Math.Round(pos.X, 3);
            PositionY = Math.Round(pos.Y, 3);
            PositionTheta = Math.Round(pos.Theta, 3);
            Driving = _coordinator.MovementEngine.IsDriving;
            DrivingDisplay = Driving ? "驶入中 (DRIVING)" : "静止 (IDLE)";

            // Record trail
            var newPt = new Point(PositionX, PositionY);
            if (TrailPoints.Count == 0)
            {
                TrailPoints.Add(newPt);
            }
            else
            {
                var last = TrailPoints[TrailPoints.Count - 1];
                double dx = newPt.X - last.X;
                double dy = newPt.Y - last.Y;
                if (dx * dx + dy * dy >= 0.0025) // ~0.05m moved
                {
                    TrailPoints.Add(newPt);
                    if (TrailPoints.Count > 800)
                    {
                        TrailPoints.RemoveAt(0);
                    }
                }
            }
        });
    }

    private void OnCoordinatorStateUpdated()
    {
        RunOnUi(() =>
        {
            bool orderChanged = !string.Equals(_lastTrackedOrderId, _coordinator.OrderId, StringComparison.OrdinalIgnoreCase);
            bool nodeChanged = !string.Equals(_lastTrackedNodeId, _coordinator.LastNodeId, StringComparison.OrdinalIgnoreCase);
            bool drivingChanged = _lastTrackedDriving != _coordinator.Driving;

            _lastTrackedOrderId = _coordinator.OrderId;
            _lastTrackedNodeId = _coordinator.LastNodeId;
            _lastTrackedDriving = _coordinator.Driving;

            OrderId = _coordinator.OrderId;
            OrderUpdateId = _coordinator.OrderUpdateId;
            LastNodeId = _coordinator.LastNodeId;
            LastNodeSequenceId = _coordinator.LastNodeSequenceId;
            Driving = _coordinator.Driving;
            Paused = _coordinator.Paused;
            OperatingMode = _coordinator.OperatingMode;
            NodeStatesCount = _coordinator.NodeStates.Count;
            EdgeStatesCount = _coordinator.EdgeStates.Count;
            Charging = _coordinator.BatterySim.Charging;
            BatteryCharge = Math.Round(_coordinator.BatterySim.BatteryCharge, 1);
            HasFatalError = _coordinator.Errors.Any(e => e.ErrorLevel == ErrorLevel.FATAL);

            UpdateDisplays();

            // Clear trail when order arrives or order cleared
            if (orderChanged)
            {
                TrailPoints.Clear();
                TrailPoints.Add(new Point(PositionX, PositionY));
            }

            // Sync Waypoints from CurrentOrder
            var currentOrder = _coordinator.CurrentOrder;
            Waypoints.Clear();
            if (currentOrder != null && currentOrder.Nodes != null && currentOrder.Nodes.Count > 0)
            {
                foreach (var n in currentOrder.Nodes)
                {
                    double x = n.NodePosition?.X ?? 0.0;
                    double y = n.NodePosition?.Y ?? 0.0;
                    Waypoints.Add(new SimWaypointItem(n.NodeId, x, y, n.Released, n.SequenceId));
                }
            }

            // Sync ActionStates per §19 (ObservableCollection on UI thread)
            ActionStates.Clear();
            foreach (var a in _coordinator.ActionStates)
            {
                ActionStates.Add(a);
            }

            // Sync Errors
            Errors.Clear();
            foreach (var e in _coordinator.Errors)
            {
                Errors.Add(e);
            }

            // Record protocol event if state or key milestones changed
            if (orderChanged || nodeChanged || drivingChanged)
            {
                AddProtocolEvent($"[状态更新/上报] 订单: {(string.IsNullOrEmpty(OrderId) ? "(无)" : OrderId)}, 批次: {OrderUpdateId}, 节点: {(string.IsNullOrEmpty(LastNodeId) ? "(无)" : LastNodeId)}, 行驶: {(Driving ? "是" : "否")}, 剩余节点: {NodeStatesCount}");
            }
        });
    }

    private void UpdateDisplays()
    {
        DrivingDisplay = Driving ? "驶入中 (DRIVING)" : "静止 (IDLE)";
        PausedDisplay = Paused ? "已暂停 (PAUSED)" : "正常运行";
        ChargingDisplay = Charging ? "充电中 (CHARGING)" : "未充电";

        OperatingModeDisplay = OperatingMode switch
        {
            OperatingMode.AUTOMATIC => "自动模式 (AUTOMATIC)",
            OperatingMode.SEMIAUTOMATIC => "半自动模式 (SEMIAUTOMATIC)",
            OperatingMode.MANUAL => "手动模式 (MANUAL)",
            OperatingMode.SERVICE => "维护模式 (SERVICE)",
            OperatingMode.TEACHIN => "示教模式 (TEACHIN)",
            _ => OperatingMode.ToString()
        };

        ConnectionStateDisplay = ConnectionState switch
        {
            ConnectionState.ONLINE => "在线 (ONLINE)",
            ConnectionState.CONNECTIONBROKEN => "异常断开 (CONNECTIONBROKEN)",
            _ => "离线 (OFFLINE)"
        };
    }

    private void OnCoordinatorLogMessage(string msg)
    {
        RunOnUi(() =>
        {
            AddLog(msg);

            // Check if this log is an order/protocol event
            if (msg.Contains("Received Order", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"📥 [收到订单] {msg}");
            }
            else if (msg.Contains("Received InstantActions", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"⚡ [收到即时动作] {msg}");
            }
            else if (msg.Contains("accepted as new active order", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"✅ [订单接受] {msg}");
            }
            else if (msg.Contains("stitched successfully", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"🔗 [订单拼接] {msg}");
            }
            else if (msg.Contains("Node reached", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"📍 [到达节点] {msg}");
            }
            else if (msg.Contains("Order traversal completed", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"🏁 [订单完成] {msg}");
            }
            else if (msg.Contains("CancelOrder", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"🛑 [取消订单] {msg}");
            }
            else if (msg.Contains("Order rejected", StringComparison.OrdinalIgnoreCase) || msg.Contains("Order validation failed", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"❌ [订单拒绝] {msg}");
            }
            else if (msg.Contains("Publishing OFFLINE", StringComparison.OrdinalIgnoreCase) || msg.Contains("ONLINE", StringComparison.OrdinalIgnoreCase))
            {
                AddProtocolEvent($"🌐 [通信状态] {msg}");
            }
        });
    }

    private void AddLog(string msg)
    {
        Logs.Insert(0, msg);
        if (Logs.Count > 300)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void AddProtocolEvent(string eventText)
    {
        string timestamped = eventText.StartsWith("[") && eventText.Length > 10 && eventText[3] == ':'
            ? eventText
            : $"[{DateTime.Now:HH:mm:ss.fff}] {eventText}";

        ProtocolLogs.Insert(0, timestamped);
        if (ProtocolLogs.Count > 300)
        {
            ProtocolLogs.RemoveAt(ProtocolLogs.Count - 1);
        }
    }

    private static void RunOnUi(System.Action action)
    {
        if (Application.Current != null && Application.Current.Dispatcher != null)
        {
            Application.Current.Dispatcher.InvokeAsync(action);
        }
        else
        {
            action();
        }
    }
}
