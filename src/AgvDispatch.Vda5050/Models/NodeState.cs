namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Represents the state of a node in a StateMessage.
/// </summary>
public class NodeState
{
    public string NodeId { get; set; } = string.Empty;

    public uint SequenceId { get; set; }

    public string? NodeDescription { get; set; }

    public bool Released { get; set; }

    public NodePosition? NodePosition { get; set; }
}
