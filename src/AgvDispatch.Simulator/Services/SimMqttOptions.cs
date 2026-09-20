namespace AgvDispatch.Simulator.Services;

/// <summary>
/// Configuration options for the Simulator MQTT client.
/// </summary>
public class SimMqttOptions
{
    public string Host { get; set; } = "[IP]";

    public int Port { get; set; } = 1883;

    public string Manufacturer { get; set; } = "AgvSim";

    public string SerialNumber { get; set; } = "AGV0001";

    public string ClientId { get; set; } = string.Empty;

    public string GetEffectiveClientId()
    {
        return string.IsNullOrWhiteSpace(ClientId) 
            ? $"AgvSim_{Manufacturer}_{SerialNumber}_{Guid.NewGuid():N}"[..Math.Min(64, 30 + SerialNumber.Length)]
            : ClientId;
    }
}
