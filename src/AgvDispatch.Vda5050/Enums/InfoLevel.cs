namespace AgvDispatch.Vda5050.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Level of an informational message. Enum member names match the VDA5050 wire values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InfoLevel
{
    INFO,
    DEBUG
}
