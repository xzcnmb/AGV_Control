namespace AgvDispatch.Vda5050.Models;

using System.Text.Json.Serialization;
using AgvDispatch.Vda5050.Enums;

/// <summary>
/// Represents the safety state of the AGV.
/// </summary>
public class SafetyState
{
    [JsonPropertyName("eStop")]
    public EStop EStop { get; set; } = EStop.NONE;

    public bool FieldViolation { get; set; } = false;
}
