namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Represents an edge connecting two nodes in an order.
/// </summary>
public class Edge
{
    public string EdgeId { get; set; } = string.Empty;

    public uint SequenceId { get; set; }

    public string? EdgeDescription { get; set; }

    public bool Released { get; set; }

    public string StartNodeId { get; set; } = string.Empty;

    public string EndNodeId { get; set; } = string.Empty;

    public double? MaxSpeed { get; set; }

    public double? Orientation { get; set; }

    public Trajectory? Trajectory { get; set; }

    public List<VdaAction> Actions { get; set; } = new();
}
