namespace AgvDispatch.MasterControl.Models;

public enum MqttDirection
{
    Incoming,
    Outgoing,
    SentEcho
}

public class MqttLogItem
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MqttDirection Direction { get; set; }
    public string DirectionDisplay => Direction switch
    {
        MqttDirection.Incoming => "IN",
        MqttDirection.Outgoing => "OUT",
        MqttDirection.SentEcho => "sent(echo)",
        _ => Direction.ToString()
    };
    public string Topic { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
