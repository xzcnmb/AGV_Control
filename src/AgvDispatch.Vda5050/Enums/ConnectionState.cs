namespace AgvDispatch.Vda5050.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Connection state of the AGV. Enum member names match the VDA5050 wire values.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectionState
{
    ONLINE,
    OFFLINE,
    CONNECTIONBROKEN
}
