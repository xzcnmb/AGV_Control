using System.Text;
using MQTTnet;
using MQTTnet.Server;
using AgvDispatch.MasterControl.Models;

namespace AgvDispatch.MasterControl.Services;

/// <summary>
/// Hosts an embedded MQTT broker on port 1883 (configurable).
/// Note: This broker is unauthenticated and intended strictly for local/LAN demo use only.
/// Exposes an event stream of intercepted publishes for the monitor's broker-side capture (§20).
/// </summary>
public class EmbeddedBroker : IAsyncDisposable
{
    private MqttServer? _server;
    private readonly int _port;

    public event Action<MqttLogItem>? MessageIntercepted;

    public bool IsRunning => _server != null;

    public EmbeddedBroker(int port = 1883)
    {
        _port = port;
    }

    public async Task StartAsync()
    {
        if (_server != null) return;

        var factory = new MqttFactory();
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(_port)
            .Build();

        _server = factory.CreateMqttServer(options);

        _server.InterceptingPublishAsync += OnInterceptingPublishAsync;

        await _server.StartAsync();
    }

    private Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic ?? string.Empty;
            var payloadBytes = args.ApplicationMessage.PayloadSegment.Array != null
                ? args.ApplicationMessage.PayloadSegment.ToArray()
                : Array.Empty<byte>();
            var payload = Encoding.UTF8.GetString(payloadBytes);
            var clientId = args.ClientId ?? string.Empty;

            var item = new MqttLogItem
            {
                Timestamp = DateTime.UtcNow,
                Direction = MqttDirection.Incoming,
                Topic = topic,
                PayloadJson = payload,
                ClientId = clientId
            };

            MessageIntercepted?.Invoke(item);
        }
        catch
        {
            // Do not throw from interceptor
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_server != null)
        {
            try
            {
                _server.InterceptingPublishAsync -= OnInterceptingPublishAsync;
                await _server.StopAsync();
            }
            finally
            {
                _server.Dispose();
                _server = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
