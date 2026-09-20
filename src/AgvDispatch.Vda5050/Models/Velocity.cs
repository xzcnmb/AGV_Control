namespace AgvDispatch.Vda5050.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Defines the velocity of the AGV.
/// </summary>
public class Velocity
{
    [JsonPropertyName("vx")]
    public double? Vx { get; set; }

    [JsonPropertyName("vy")]
    public double? Vy { get; set; }

    [JsonPropertyName("omega")]
    public double? Omega { get; set; }
}
