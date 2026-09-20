using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.MasterControl.Services;

namespace AgvDispatch.MasterControl.ViewModels;

public partial class TaskHistoryViewModel : ObservableObject
{
    private readonly TaskRepository _repository;

    public ObservableCollection<OrderRecord> Orders { get; } = new();
    public ObservableCollection<OrderEventRecord> OrderEvents { get; } = new();

    [ObservableProperty]
    private OrderRecord? _selectedOrder;

    public TaskHistoryViewModel(TaskRepository repository)
    {
        _repository = repository;
        Refresh();
    }

    partial void OnSelectedOrderChanged(OrderRecord? value)
    {
        OrderEvents.Clear();
        if (value != null)
        {
            var events = _repository.GetOrderEvents(value.OrderId);
            foreach (var ev in events)
            {
                OrderEvents.Add(ev);
            }
        }
    }

    [RelayCommand]
    public void Refresh()
    {
        Orders.Clear();
        var orders = _repository.GetOrders(100);
        foreach (var ord in orders)
        {
            Orders.Add(ord);
        }

        if (SelectedOrder != null)
        {
            var matched = Orders.FirstOrDefault(o => o.OrderId == SelectedOrder.OrderId);
            SelectedOrder = matched;
        }
    }
}
