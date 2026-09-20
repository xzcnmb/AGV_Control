using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.MasterControl.Services;

namespace AgvDispatch.MasterControl.ViewModels;

public partial class AgvListViewModel : ObservableObject
{
    private readonly AgvRegistry _registry;
    private readonly OrderService _orderService;

    public ObservableCollection<AgvEntry> Agvs => _registry.AgvList;

    [ObservableProperty]
    private AgvEntry? _selectedAgv;

    [ObservableProperty]
    private string _actionStatusMessage = string.Empty;

    public event Action<AgvEntry?>? SelectedAgvChanged;

    public AgvListViewModel(AgvRegistry registry, OrderService orderService)
    {
        _registry = registry;
        _orderService = orderService;
    }

    partial void OnSelectedAgvChanged(AgvEntry? value)
    {
        SelectedAgvChanged?.Invoke(value);
    }

    [RelayCommand]
    public async Task StartPauseAsync()
    {
        if (SelectedAgv == null) return;
        ActionStatusMessage = $"正在向 {SelectedAgv.SerialNumber} 发送暂停指令 (startPause)...";
        await _orderService.SendStartPauseAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        ActionStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送暂停指令。";
    }

    [RelayCommand]
    public async Task StopPauseAsync()
    {
        if (SelectedAgv == null) return;
        ActionStatusMessage = $"正在向 {SelectedAgv.SerialNumber} 发送恢复指令 (stopPause)...";
        await _orderService.SendStopPauseAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        ActionStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送恢复指令。";
    }

    [RelayCommand]
    public async Task CancelOrderAsync()
    {
        if (SelectedAgv == null) return;
        ActionStatusMessage = $"正在向 {SelectedAgv.SerialNumber} 发送取消订单指令 (cancelOrder)...";
        await _orderService.SendCancelOrderAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        ActionStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送取消指令，正在监听10秒取消握手 (§13)。";
    }

    [RelayCommand]
    public async Task RequestFactsheetAsync()
    {
        if (SelectedAgv == null) return;
        ActionStatusMessage = $"正在向 {SelectedAgv.SerialNumber} 请求参数说明表 (Factsheet)...";
        await _orderService.SendFactsheetRequestAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        ActionStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送 Factsheet 请求。";
    }

    [RelayCommand]
    public async Task RequestStateAsync()
    {
        if (SelectedAgv == null) return;
        ActionStatusMessage = $"正在向 {SelectedAgv.SerialNumber} 请求状态 (State)...";
        await _orderService.SendStateRequestAsync(SelectedAgv.Manufacturer, SelectedAgv.SerialNumber);
        ActionStatusMessage = $"已向 {SelectedAgv.SerialNumber} 发送 State 请求。";
    }
}
