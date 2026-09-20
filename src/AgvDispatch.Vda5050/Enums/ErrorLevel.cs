namespace AgvDispatch.Vda5050.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Level of an error. Enum member names match the VDA5050 wire values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorLevel
{
    WARNING,
    FATAL
}
