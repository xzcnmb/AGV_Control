using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.Vda5050.Json;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Topics;

namespace AgvDispatch.MasterControl.Services;

/// <summary>
/// Master's own MQTT client service.
/// Connects to embedded broker ([IP]:1883) with retry/backoff.
/// Subscribes to uagv/v2/#.
/// Serialized single-writer publisher stamping headerId (§17, §19).
/// </summary>
public class MqttClientService : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;
    private readonly HeaderIdCounter _headerIdCounter = new();
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly string _masterClientId;
    private bool _isDisposed;
    private bool _explicitDisconnect;

    public event Action<ConnectionMessage>? ConnectionReceived;
    public event Action<StateMessage>? StateReceived;
    public event Action<FactsheetMessage>? FactsheetReceived;
    public event Action<VisualizationMessage>? VisualizationReceived;
    public event Action<MqttLogItem>? ClientMessageLogged;
    public event Action<bool>? ConnectionStatusChanged;

    public bool IsConnected => _client.IsConnected;

    public MqttClientService(string brokerHost = "[IP]", int brokerPort = 1883, string clientId = "MasterControl")
    {
        _masterClientId = clientId;
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        _clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId(clientId)
            .WithCleanSession()
            .Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ConnectedAsync += OnConnectedAsync;
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        ConnectionStatusChanged?.Invoke(true);
        return Task.CompletedTask;
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        ConnectionStatusChanged?.Invoke(false);

        if (_explicitDisconnect || _isDisposed)
        {
            return;
        }

        // Reconnect with backoff
        _ = Task.Run(async () =>
        {
            int delay = 1000;
            while (!_isDisposed && !_explicitDisconnect && !_client.IsConnected)
            {
                try
                {
                    await Task.Delay(delay);
                    await _client.ConnectAsync(_clientOptions);
                    await _client.SubscribeAsync(Vda5050Topic.Wildcard, MqttQualityOfServiceLevel.AtLeastOnce);
                    break;
                }
                catch
                {
                    delay = Math.Min(delay * 2, 10000);
                }
            }
        });
    }

    public async Task ConnectAsync()
    {
        _explicitDisconnect = false;
        int retries = 5;
        int delay = 500;
        for (int i = 0; i < retries; i++)
        {
            try
            {
                await _client.ConnectAsync(_clientOptions);
                await _client.SubscribeAsync(Vda5050Topic.Wildcard, MqttQualityOfServiceLevel.AtLeastOnce);
                return;
            }
            catch when (i < retries - 1)
            {
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 3000);
            }
        }
    }

    public async Task DisconnectAsync()
    {
        _explicitDisconnect = true;
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic ?? string.Empty;
            var payloadBytes = args.ApplicationMessage.PayloadSegment.Array != null
                ? args.ApplicationMessage.PayloadSegment.ToArray()
                : Array.Empty<byte>();
            var payload = Encoding.UTF8.GetString(payloadBytes);

            // Parse VDA5050 topic
            if (!Vda5050Topic.TryParse(topic, out var topicInfo))
            {
                return Task.CompletedTask;
            }

            // Check if this is an echo of master's own published order or instantActions (§20)
            if (topicInfo.Topic == Vda5050Topic.Order || topicInfo.Topic == Vda5050Topic.InstantActions)
            {
                ClientMessageLogged?.Invoke(new MqttLogItem
                {
                    Timestamp = DateTime.UtcNow,
                    Direction = MqttDirection.SentEcho,
                    Topic = topic,
                    PayloadJson = payload,
                    ClientId = _masterClientId
                });
                return Task.CompletedTask;
            }

            // Parse message per topic (§7, §11, etc.)
            switch (topicInfo.Topic)
            {
                case Vda5050Topic.Connection:
                    var conn = Vda5050Json.Deserialize<ConnectionMessage>(payload);
                    if (conn != null) ConnectionReceived?.Invoke(conn);
                    break;

                case Vda5050Topic.State:
                    var state = Vda5050Json.Deserialize<StateMessage>(payload);
                    if (state != null) StateReceived?.Invoke(state);
                    break;

                case Vda5050Topic.Factsheet:
                    var factsheet = Vda5050Json.Deserialize<FactsheetMessage>(payload);
                    if (factsheet != null) FactsheetReceived?.Invoke(factsheet);
                    break;

                case Vda5050Topic.Visualization:
                    var vis = Vda5050Json.Deserialize<VisualizationMessage>(payload);
                    if (vis != null) VisualizationReceived?.Invoke(vis);
                    break;
            }
        }
        catch
        {
            // §19: MQTT callback parsing failure logs and does not throw out
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Single-writer serialized Publish stamping headerId (§17, §19).
    /// </summary>
    public async Task PublishAsync<T>(string manufacturer, string serialNumber, string subTopic, T message, int qos = 0, bool retain = false)
        where T : Vda5050Header
    {
        await _publishLock.WaitAsync();
        try
        {
            var topic = Vda5050Topic.Build(manufacturer, serialNumber, subTopic);
            _headerIdCounter.Stamp(message, subTopic, manufacturer, serialNumber);

            var json = Vda5050Json.Serialize(message);

            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(json)
                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
                .WithRetainFlag(retain)
                .Build();

            await _client.PublishAsync(msg);

            // Log client's outgoing publish to monitor (§20)
            ClientMessageLogged?.Invoke(new MqttLogItem
            {
                Timestamp = DateTime.UtcNow,
                Direction = MqttDirection.Outgoing,
                Topic = topic,
                PayloadJson = json,
                ClientId = _masterClientId
            });
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _explicitDisconnect = true;

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }
            _client.Dispose();
            _publishLock.Dispose();
        }
        catch
        {
            // Dispose safely
        }
    }
}
