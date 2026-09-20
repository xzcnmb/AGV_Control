namespace AgvDispatch.Vda5050.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Defines the position of a node on a map.
/// </summary>
public class NodePosition
{
    public double X { get; set; }

    public double Y { get; set; }

    public double? Theta { get; set; }

    public string MapId { get; set; } = string.Empty;

    [JsonPropertyName("allowedDeviationXY")]
    public double? AllowedDeviationXY { get; set; }

    [JsonPropertyName("allowedDeviationTheta")]
    public double? AllowedDeviationTheta { get; set; }

    public string? MapDescription { get; set; }
}
