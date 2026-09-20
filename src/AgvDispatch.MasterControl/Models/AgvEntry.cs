using CommunityToolkit.Mvvm.ComponentModel;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;

namespace AgvDispatch.MasterControl.Models;

public enum MasterConnectionState
{
    Unknown,
    Online,
    Offline,
    ConnectionBroken
}

public partial class AgvEntry : ObservableObject
{
    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _serialNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStateDisplay))]
    private MasterConnectionState _connectionState = MasterConnectionState.Unknown;

    public string ConnectionStateDisplay => ConnectionState switch
    {
        MasterConnectionState.Online => "在线",
        MasterConnectionState.Offline => "离线",
        MasterConnectionState.ConnectionBroken => "连接断开",
        _ => "未知"
    };

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private DateTime? _lastStateTimestamp;

    [ObservableProperty]
    private DateTime? _lastMessageTimestamp;

    [ObservableProperty]
    private double _batteryCharge;

    [ObservableProperty]
    private bool _isCharging;

    [ObservableProperty]
    private bool _driving;

    [ObservableProperty]
    private bool _paused;

    [ObservableProperty]
    private string _orderId = string.Empty;

    [ObservableProperty]
    private uint _orderUpdateId;

    [ObservableProperty]
    private string _lastNodeId = string.Empty;

    [ObservableProperty]
    private uint _lastNodeSequenceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperatingModeDisplay))]
    private OperatingMode _operatingMode = OperatingMode.AUTOMATIC;

    public string OperatingModeDisplay => OperatingMode switch
    {
        OperatingMode.AUTOMATIC => "自动模式",
        OperatingMode.SEMIAUTOMATIC => "半自动模式",
        OperatingMode.MANUAL => "手动模式",
        OperatingMode.SERVICE => "维护模式",
        OperatingMode.TEACHIN => "示教模式",
        _ => OperatingMode.ToString()
    };

    [ObservableProperty]
    private ErrorLevel? _highestErrorLevel;

    [ObservableProperty]
    private bool _hasFatalError;

    [ObservableProperty]
    private bool _hasWarning;

    [ObservableProperty]
    private string _errorSummary = string.Empty;

    [ObservableProperty]
    private AgvPosition? _positionSnapshot;

    [ObservableProperty]
    private StateMessage? _latestState;

    [ObservableProperty]
    private FactsheetMessage? _factsheet;

    public void UpdateConnection(ConnectionState state)
    {
        ConnectionState = state switch
        {
            AgvDispatch.Vda5050.Enums.ConnectionState.ONLINE => MasterConnectionState.Online,
            AgvDispatch.Vda5050.Enums.ConnectionState.OFFLINE => MasterConnectionState.Offline,
            AgvDispatch.Vda5050.Enums.ConnectionState.CONNECTIONBROKEN => MasterConnectionState.ConnectionBroken,
            _ => MasterConnectionState.Unknown
        };

        if (ConnectionState == MasterConnectionState.Online)
        {
            IsStale = false;
        }

        LastMessageTimestamp = DateTime.UtcNow;
    }

    public void UpdateState(StateMessage msg)
    {
        LatestState = msg;
        LastStateTimestamp = DateTime.UtcNow;
        LastMessageTimestamp = DateTime.UtcNow;
        IsStale = false;

        // If we received state, the AGV is definitely online
        if (ConnectionState != MasterConnectionState.Online)
        {
            ConnectionState = MasterConnectionState.Online;
        }

        OrderId = msg.OrderId ?? string.Empty;
        OrderUpdateId = msg.OrderUpdateId;
        LastNodeId = msg.LastNodeId ?? string.Empty;
        LastNodeSequenceId = msg.LastNodeSequenceId;
        Driving = msg.Driving;
        Paused = msg.Paused ?? false;
        OperatingMode = msg.OperatingMode;

        if (msg.BatteryState != null)
        {
            BatteryCharge = msg.BatteryState.BatteryCharge;
            IsCharging = msg.BatteryState.Charging;
        }

        if (msg.AgvPosition != null)
        {
            // Thread-safe copy of position
            PositionSnapshot = new AgvPosition
            {
                X = msg.AgvPosition.X,
                Y = msg.AgvPosition.Y,
                Theta = msg.AgvPosition.Theta,
                MapId = msg.AgvPosition.MapId,
                PositionInitialized = msg.AgvPosition.PositionInitialized,
                LocalizationScore = msg.AgvPosition.LocalizationScore,
                DeviationRange = msg.AgvPosition.DeviationRange,
                MapDescription = msg.AgvPosition.MapDescription
            };
        }

        // Evaluate errors
        HasFatalError = msg.Errors != null && msg.Errors.Any(e => e.ErrorLevel == ErrorLevel.FATAL);
        HasWarning = msg.Errors != null && msg.Errors.Any(e => e.ErrorLevel == ErrorLevel.WARNING);

        if (HasFatalError)
        {
            HighestErrorLevel = ErrorLevel.FATAL;
        }
        else if (HasWarning)
        {
            HighestErrorLevel = ErrorLevel.WARNING;
        }
        else
        {
            HighestErrorLevel = null;
        }

        if (msg.Errors != null && msg.Errors.Count > 0)
        {
            ErrorSummary = string.Join("; ", msg.Errors.Select(e => $"{e.ErrorLevel}: {e.ErrorType} - {e.ErrorDescription}"));
        }
        else
        {
            ErrorSummary = string.Empty;
        }
    }

    public void UpdateVisualization(VisualizationMessage vis)
    {
        LastMessageTimestamp = DateTime.UtcNow;
        if (vis.AgvPosition != null)
        {
            PositionSnapshot = new AgvPosition
            {
                X = vis.AgvPosition.X,
                Y = vis.AgvPosition.Y,
                Theta = vis.AgvPosition.Theta,
                MapId = vis.AgvPosition.MapId,
                PositionInitialized = vis.AgvPosition.PositionInitialized,
                LocalizationScore = vis.AgvPosition.LocalizationScore,
                DeviationRange = vis.AgvPosition.DeviationRange,
                MapDescription = vis.AgvPosition.MapDescription
            };
        }
    }

    public void UpdateFactsheet(FactsheetMessage factsheet)
    {
        Factsheet = factsheet;
        LastMessageTimestamp = DateTime.UtcNow;
    }

    public void CheckWatchdog(DateTime now, double timeoutSeconds = 60.0)
    {
        if (ConnectionState == MasterConnectionState.Online)
        {
            if (LastStateTimestamp.HasValue && (now - LastStateTimestamp.Value).TotalSeconds > timeoutSeconds)
            {
                IsStale = true;
                ConnectionState = MasterConnectionState.ConnectionBroken;
            }
        }
    }
}
