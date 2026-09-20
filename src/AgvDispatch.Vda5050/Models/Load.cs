namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Represents a load carried by the AGV.
/// </summary>
public class Load
{
    public string? LoadId { get; set; }

    public string? LoadType { get; set; }

    public string? LoadPosition { get; set; }

    public double? Weight { get; set; }
}
