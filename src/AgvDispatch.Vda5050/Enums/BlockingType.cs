namespace AgvDispatch.Vda5050.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Blocking type of an action. Enum member names match the VDA5050 wire values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlockingType
{
    NONE,
    SOFT,
    HARD
}
