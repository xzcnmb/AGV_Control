namespace AgvDispatch.Vda5050.Messages;

using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Models;

/// <summary>
/// State message published periodically or on event by the AGV to report its current state.
/// </summary>
public class StateMessage : Vda5050Header
{
    // Required fields (always serialized, even when empty or default)

    public string OrderId { get; set; } = string.Empty;

    public uint OrderUpdateId { get; set; }

    public string LastNodeId { get; set; } = string.Empty;

    public uint LastNodeSequenceId { get; set; }

    public bool Driving { get; set; }

    public List<NodeState> NodeStates { get; set; } = new();

    public List<EdgeState> EdgeStates { get; set; } = new();

    public List<ActionState> ActionStates { get; set; } = new();

    public BatteryState BatteryState { get; set; } = new();

    public OperatingMode OperatingMode { get; set; } = OperatingMode.AUTOMATIC;

    public List<Error> Errors { get; set; } = new();

    public SafetyState SafetyState { get; set; } = new();

    public List<Info> Information { get; set; } = new();

    // Optional fields (omitted when null)

    public AgvPosition? AgvPosition { get; set; }

    public Velocity? Velocity { get; set; }

    public List<Load>? Loads { get; set; }

    public bool? Paused { get; set; }

    public bool? NewBaseRequest { get; set; }

    public double? DistanceSinceLastNode { get; set; }

    public string? ZoneSetId { get; set; }
}
