using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.MasterControl.Services;
using AgvDispatch.Vda5050.StateMachines;

namespace AgvDispatch.MasterControl.ViewModels;

public partial class OrderEditorViewModel : ObservableObject
{
    private readonly OrderService _orderService;
    private readonly AgvRegistry _agvRegistry;

    public ObservableCollection<AgvEntry> AvailableAgvs => _agvRegistry.AgvList;

    [ObservableProperty]
    private AgvEntry? _selectedAgv;

    [ObservableProperty]
    private string _orderId = string.Empty;

    [ObservableProperty]
    private int _baseSize = 2; // §12b: base >= 2

    [ObservableProperty]
    private double _newWaypointX;

    [ObservableProperty]
    private double _newWaypointY;

    [ObservableProperty]
    private string _validationStatus = "No waypoints defined.";

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string _dispatchStatusMessage = string.Empty;

    public ObservableCollection<WaypointItem> Waypoints { get; } = new();

    public event Action? WaypointsChanged;

    public OrderEditorViewModel(OrderService orderService, AgvRegistry agvRegistry)
    {
        _orderService = orderService;
        _agvRegistry = agvRegistry;

        GenerateNewOrderId();
        LoadPresetLine();
    }

    [RelayCommand]
    public void GenerateNewOrderId()
    {
        OrderId = "ORD-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Random.Shared.Next(100, 999);
    }

    [RelayCommand]
    public void AddWaypoint()
    {
        int index = Waypoints.Count;
        string nodeId = $"node_{index}";
        Waypoints.Add(new WaypointItem(nodeId, NewWaypointX, NewWaypointY, index < BaseSize));
        ValidateOrder();
        WaypointsChanged?.Invoke();
    }

    public void AddWaypointFromMap(double x, double y)
    {
        int index = Waypoints.Count;
        string nodeId = $"node_{index}";
        // Round to 2 decimal places
        x = Math.Round(x, 2);
        y = Math.Round(y, 2);
        Waypoints.Add(new WaypointItem(nodeId, x, y, index < BaseSize));
        ValidateOrder();
        WaypointsChanged?.Invoke();
    }

    [RelayCommand]
    public void RemoveWaypoint(WaypointItem? item)
    {
        if (item != null && Waypoints.Remove(item))
        {
            ReindexWaypoints();
            ValidateOrder();
            WaypointsChanged?.Invoke();
        }
    }

    [RelayCommand]
    public void ClearWaypoints()
    {
        Waypoints.Clear();
        ValidateOrder();
        WaypointsChanged?.Invoke();
    }

    [RelayCommand]
    public void LoadPresetLine()
    {
        Waypoints.Clear();
        Waypoints.Add(new WaypointItem("node_0", 2.0, 2.0, true));
        Waypoints.Add(new WaypointItem("node_1", 6.0, 2.0, true));
        Waypoints.Add(new WaypointItem("node_2", 10.0, 2.0, false));
        Waypoints.Add(new WaypointItem("node_3", 14.0, 2.0, false));
        ReindexWaypoints();
        ValidateOrder();
        WaypointsChanged?.Invoke();
    }

    [RelayCommand]
    public void LoadPresetSquare()
    {
        Waypoints.Clear();
        Waypoints.Add(new WaypointItem("node_0", 2.0, 2.0, true));
        Waypoints.Add(new WaypointItem("node_1", 10.0, 2.0, true));
        Waypoints.Add(new WaypointItem("node_2", 10.0, 10.0, false));
        Waypoints.Add(new WaypointItem("node_3", 2.0, 10.0, false));
        Waypoints.Add(new WaypointItem("node_4", 2.0, 2.0, false));
        ReindexWaypoints();
        ValidateOrder();
        WaypointsChanged?.Invoke();
    }

    private void ReindexWaypoints()
    {
        for (int i = 0; i < Waypoints.Count; i++)
        {
            Waypoints[i].NodeId = $"node_{i}";
            Waypoints[i].IsReleased = i < BaseSize;
        }
    }

    partial void OnBaseSizeChanged(int value)
    {
        if (value < 2)
        {
            BaseSize = 2;
            return;
        }
        ReindexWaypoints();
        ValidateOrder();
        WaypointsChanged?.Invoke();
    }

    public void ValidateOrder()
    {
        if (Waypoints.Count < 2)
        {
            IsValid = false;
            ValidationStatus = "订单至少需要2个路径节点 (边数 = 节点数 - 1)。";
            return;
        }

        var wpTuples = Waypoints.Select(w => (w.NodeId, w.X, w.Y)).ToList();
        var (order, validation) = OrderService.CreateLinearOrder(
            OrderId,
            SelectedAgv?.Manufacturer ?? "AgvSim",
            SelectedAgv?.SerialNumber ?? "AGV0001",
            wpTuples,
            BaseSize
        );

        IsValid = validation.IsValid;
        if (IsValid)
        {
            int releasedCount = order.Nodes.Count(n => n.Released);
            int horizonCount = order.Nodes.Count - releasedCount;
            ValidationStatus = $"校验通过！共 {order.Nodes.Count} 个节点，{order.Edges.Count} 条边 (已释放基准 Base: {releasedCount}，远景 Horizon: {horizonCount})。";
        }
        else
        {
            ValidationStatus = "校验失败: " + string.Join("; ", validation.Errors);
        }
    }

    [RelayCommand]
    public async Task DispatchOrderAsync()
    {
        if (SelectedAgv == null)
        {
            DispatchStatusMessage = "请先选择目标 AGV。";
            return;
        }

        if (Waypoints.Count < 2)
        {
            DispatchStatusMessage = "无法下发：至少需要2个路径点。";
            return;
        }

        var wpTuples = Waypoints.Select(w => (w.NodeId, w.X, w.Y)).ToList();
        var (order, validation) = OrderService.CreateLinearOrder(
            OrderId,
            SelectedAgv.Manufacturer,
            SelectedAgv.SerialNumber,
            wpTuples,
            BaseSize
        );

        if (!validation.IsValid)
        {
            DispatchStatusMessage = "无法下发非法订单: " + string.Join("; ", validation.Errors);
            return;
        }

        DispatchStatusMessage = $"正在向下发订单 '{order.OrderId}' 至 '{SelectedAgv.SerialNumber}'...";
        var (success, error) = await _orderService.DispatchOrderAsync(order, BaseSize);
        if (success)
        {
            DispatchStatusMessage = $"订单 '{order.OrderId}' 下发成功！等待 AGV 受理确认...";
            GenerateNewOrderId();
        }
        else
        {
            DispatchStatusMessage = $"下发失败: {error}";
        }
    }

    [RelayCommand]
    public async Task StartPauseAsync()
    {
        if (SelectedAgv == null) return;
        await _orderService.SendStartPauseAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        DispatchStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送暂停指令。";
    }

    [RelayCommand]
    public async Task StopPauseAsync()
    {
        if (SelectedAgv == null) return;
        await _orderService.SendStopPauseAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        DispatchStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送恢复指令。";
    }

    [RelayCommand]
    public async Task CancelOrderAsync()
    {
        if (SelectedAgv == null) return;
        await _orderService.SendCancelOrderAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        DispatchStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送取消指令，正在监听10秒取消握手 (§13)。";
    }

    [RelayCommand]
    public async Task RequestFactsheetAsync()
    {
        if (SelectedAgv == null) return;
        await _orderService.SendFactsheetRequestAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        DispatchStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送 Factsheet 请求。";
    }

    [RelayCommand]
    public async Task RequestStateAsync()
    {
        if (SelectedAgv == null) return;
        await _orderService.SendStateRequestAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        DispatchStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送 State 请求。";
    }
}
