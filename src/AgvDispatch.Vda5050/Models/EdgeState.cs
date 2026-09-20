namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Represents the state of an edge in a StateMessage.
/// </summary>
public class EdgeState
{
    public string EdgeId { get; set; } = string.Empty;

    public uint SequenceId { get; set; }

    public string? EdgeDescription { get; set; }

    public bool Released { get; set; }

    public Trajectory? Trajectory { get; set; }
}
