using CommunityToolkit.Mvvm.ComponentModel;

namespace AgvDispatch.MasterControl.Models;

public partial class WaypointItem : ObservableObject
{
    [ObservableProperty]
    private string _nodeId = string.Empty;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private bool _isReleased;

    public WaypointItem() { }

    public WaypointItem(string nodeId, double x, double y, bool isReleased = true)
    {
        _nodeId = nodeId;
        _x = x;
        _y = y;
        _isReleased = isReleased;
    }
}
