namespace AgvDispatch.Vda5050.Topics;

/// <summary>
/// Parsed information from a VDA5050 MQTT topic.
/// </summary>
public readonly record struct Vda5050TopicInfo(string Manufacturer, string SerialNumber, string Topic)
{
    public static Vda5050TopicInfo Empty => new(string.Empty, string.Empty, string.Empty);
}
