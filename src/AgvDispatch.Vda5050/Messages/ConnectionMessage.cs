namespace AgvDispatch.Vda5050.Messages;

using AgvDispatch.Vda5050.Enums;

/// <summary>
/// Connection message published to describe the AGV's connection state.
/// </summary>
public class ConnectionMessage : Vda5050Header
{
    public ConnectionState ConnectionState { get; set; } = ConnectionState.ONLINE;
}
