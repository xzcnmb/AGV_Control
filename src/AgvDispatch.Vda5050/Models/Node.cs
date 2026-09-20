namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Represents a node in an order or topology.
/// </summary>
public class Node
{
    public string NodeId { get; set; } = string.Empty;

    public uint SequenceId { get; set; }

    public string? NodeDescription { get; set; }

    public bool Released { get; set; }

    public NodePosition? NodePosition { get; set; }

    public List<VdaAction> Actions { get; set; } = new();
}
