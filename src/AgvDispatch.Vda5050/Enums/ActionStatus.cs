namespace AgvDispatch.Vda5050.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Status of an action. Enum member names match the VDA5050 wire values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionStatus
{
    WAITING,
    INITIALIZING,
    RUNNING,
    PAUSED,
    FINISHED,
    FAILED
}
