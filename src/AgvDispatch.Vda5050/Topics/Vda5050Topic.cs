namespace AgvDispatch.Vda5050.Topics;

/// <summary>
/// Helper for constructing and parsing VDA5050 MQTT topics:
/// interfaceName/majorVersion/manufacturer/serialNumber/topic
/// </summary>
public static class Vda5050Topic
{
    public const string InterfaceName = "uagv";

    public const string MajorVersion = "v2";

    public const string Connection = "connection";

    public const string Factsheet = "factsheet";

    public const string State = "state";

    public const string Order = "order";

    public const string InstantActions = "instantActions";

    public const string Visualization = "visualization";

    /// <summary>
    /// Wildcard topic to subscribe to all VDA5050 messages.
    /// </summary>
    public const string Wildcard = "uagv/v2/#";

    /// <summary>
    /// Builds a VDA5050 MQTT topic.
    /// </summary>
    /// <param name="manufacturer">AGV manufacturer name (e.g. AgvSim)</param>
    /// <param name="serialNumber">AGV serial number (e.g. AGV0001)</param>
    /// <param name="topic">Message topic (e.g. state, order, connection)</param>
    public static string Build(string manufacturer, string serialNumber, string topic)
    {
        return $"{InterfaceName}/{MajorVersion}/{manufacturer}/{serialNumber}/{topic}";
    }

    /// <summary>
    /// Attempts to parse a VDA5050 MQTT topic string into its constituent parts.
    /// </summary>
    public static bool TryParse(string? topic, out Vda5050TopicInfo info)
    {
        if (string.IsNullOrEmpty(topic))
        {
            info = Vda5050TopicInfo.Empty;
            return false;
        }

        var parts = topic.Split('/');
        if (parts.Length == 5 &&
            parts[0] == InterfaceName &&
            parts[1] == MajorVersion &&
            !string.IsNullOrEmpty(parts[2]) &&
            !string.IsNullOrEmpty(parts[3]) &&
            !string.IsNullOrEmpty(parts[4]))
        {
            info = new Vda5050TopicInfo(parts[2], parts[3], parts[4]);
            return true;
        }

        info = Vda5050TopicInfo.Empty;
        return false;
    }
}
