namespace AgvDispatch.Vda5050.Json;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Converts DateTime to and from ISO 8601 UTC format: yyyy-MM-dd'T'HH:mm:ss.fff'Z'.
/// </summary>
public sealed class Vda5050DateTimeConverter : JsonConverter<DateTime>
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrEmpty(str))
        {
            return default;
        }

        return DateTime.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
    }
}
