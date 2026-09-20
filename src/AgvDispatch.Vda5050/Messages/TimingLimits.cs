namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Timing limits for state reporting and order updates.
/// </summary>
public class TimingLimits
{
    public double? MinOrderInterval { get; set; }

    public double? MinStateInterval { get; set; }

    public double DefaultStateInterval { get; set; } = 30.0;

    public double VisualizationInterval { get; set; } = 0.2;
}
