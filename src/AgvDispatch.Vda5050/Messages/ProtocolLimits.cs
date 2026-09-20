namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Protocol limits and timing specifications for the AGV.
/// </summary>
public class ProtocolLimits
{
    public TimingLimits Timing { get; set; } = new();

    public Dictionary<string, int>? MaxStringLens { get; set; }

    public Dictionary<string, int>? MaxArrayLens { get; set; }
}
