namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Defines the position of the AGV.
/// </summary>
public class AgvPosition
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Theta { get; set; }

    public string MapId { get; set; } = string.Empty;

    public bool PositionInitialized { get; set; }

    public double? LocalizationScore { get; set; }

    public double? DeviationRange { get; set; }

    public string? MapDescription { get; set; }
}
