namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Base header shared by all VDA5050 messages.
/// </summary>
public abstract class Vda5050Header
{
    public uint HeaderId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string Version { get; set; } = Vda5050Constants.ProtocolVersion;

    public string Manufacturer { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;
}
