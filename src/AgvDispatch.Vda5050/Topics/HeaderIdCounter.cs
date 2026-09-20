namespace AgvDispatch.Vda5050.Topics;

using AgvDispatch.Vda5050.Messages;

/// <summary>
/// Thread-safe per-topic HeaderId counter according to VDA5050 §17.
/// </summary>
public class HeaderIdCounter
{
    private readonly object _lock = new();
    private readonly Dictionary<string, uint> _counters = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the next headerId for the specified topic and increments the counter.
    /// The first call for a topic returns 0, subsequent calls return 1, 2, 3...
    /// </summary>
    public uint Next(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        lock (_lock)
        {
            if (!_counters.TryGetValue(topic, out var current))
            {
                current = 0;
                _counters[topic] = 1;
                return current;
            }

            _counters[topic] = current + 1;
            return current;
        }
    }

    /// <summary>
    /// Gets the current counter value for a topic without incrementing.
    /// </summary>
    public uint GetCurrent(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        lock (_lock)
        {
            return _counters.TryGetValue(topic, out var current) ? current : 0;
        }
    }

    /// <summary>
    /// Resets the counter for a specific topic back to 0.
    /// </summary>
    public void Reset(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        lock (_lock)
        {
            _counters[topic] = 0;
        }
    }

    /// <summary>
    /// Resets all topic counters.
    /// </summary>
    public void ResetAll()
    {
        lock (_lock)
        {
            _counters.Clear();
        }
    }

    /// <summary>
    /// Stamps a message header with the next headerId for the topic, current UTC timestamp,
    /// protocol version, manufacturer, and serial number.
    /// </summary>
    public void Stamp(Vda5050Header msg, string topic, string manufacturer, string serialNumber)
    {
        ArgumentNullException.ThrowIfNull(msg);
        ArgumentNullException.ThrowIfNull(topic);

        msg.HeaderId = Next(topic);
        msg.Timestamp = DateTime.UtcNow;
        msg.Version = Vda5050Constants.ProtocolVersion;
        msg.Manufacturer = manufacturer ?? string.Empty;
        msg.SerialNumber = serialNumber ?? string.Empty;
    }
}
