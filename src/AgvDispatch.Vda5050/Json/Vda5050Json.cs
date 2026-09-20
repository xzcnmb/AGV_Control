namespace AgvDispatch.Vda5050.Json;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Central entry point for VDA5050 JSON serialization and deserialization.
/// </summary>
public static class Vda5050Json
{
    /// <summary>
    /// Shared JsonSerializerOptions configured according to VDA5050 v2.0.0 rules:
    /// camelCase properties, string enums, UTC ISO8601 millisecond timestamps, nulls omitted.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new Vda5050DateTimeConverter());
        options.Converters.Add(new Vda5050DateTimeOffsetConverter());

        return options;
    }

    /// <summary>
    /// Serializes an object to a VDA5050 compliant JSON string.
    /// </summary>
    public static string Serialize<T>(T msg)
    {
        return JsonSerializer.Serialize(msg, Options);
    }

    /// <summary>
    /// Deserializes a VDA5050 compliant JSON string to the specified type.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
