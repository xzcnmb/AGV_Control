namespace AgvDispatch.Simulator.Models;

/// <summary>
/// Represents a waypoint in the simulator's view of an order route.
/// </summary>
public class SimWaypointItem
{
    public string NodeId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsReleased { get; set; }
    public uint SequenceId { get; set; }

    public SimWaypointItem() { }

    public SimWaypointItem(string nodeId, double x, double y, bool isReleased, uint sequenceId = 0)
    {
        NodeId = nodeId;
        X = x;
        Y = y;
        IsReleased = isReleased;
        SequenceId = sequenceId;
    }
}
