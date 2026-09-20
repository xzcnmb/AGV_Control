using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.MasterControl.Services;

namespace AgvDispatch.MasterControl.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly AgvRegistry _registry;
    private readonly OrderEditorViewModel _orderEditor;

    public ObservableCollection<AgvEntry> Agvs => _registry.AgvList;
    public ObservableCollection<WaypointItem> PlannedWaypoints => _orderEditor.Waypoints;

    [ObservableProperty]
    private AgvEntry? _selectedAgv;

    [ObservableProperty]
    private double _extentMinX = -2.0;

    [ObservableProperty]
    private double _extentMaxX = 22.0;

    [ObservableProperty]
    private double _extentMinY = -2.0;

    [ObservableProperty]
    private double _extentMaxY = 22.0;

    public MapViewModel(AgvRegistry registry, OrderEditorViewModel orderEditor)
    {
        _registry = registry;
        _orderEditor = orderEditor;

        _orderEditor.WaypointsChanged += () =>
        {
            OnPropertyChanged(nameof(PlannedWaypoints));
        };
    }

    public void OnMapClicked(double worldX, double worldY)
    {
        _orderEditor.AddWaypointFromMap(worldX, worldY);
    }
}
