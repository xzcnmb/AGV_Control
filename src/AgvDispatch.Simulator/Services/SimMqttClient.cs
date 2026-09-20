namespace AgvDispatch.Simulator.Services;

using System.Diagnostics;
using System.Text;
using AgvDispatch.Vda5050;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Json;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Topics;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

/// <summary>
/// Wraps MQTTnet IMqttClient implementing VDA5050 §5, §11, §17, §19:
/// - Connects to broker with Last-Will (QoS1 retained CONNECTIONBROKEN)
/// - Publishes ONLINE (QoS1 retained) on connect/reconnect
/// - Publishes OFFLINE (QoS1 retained) then disconnects on graceful shutdown
/// - Reconnect with exponential backoff
/// - Subscribes to order and instantActions topics
/// - Thread-safe single-writer publish with HeaderIdCounter stamping
/// - All received-message handling exception-guarded
/// </summary>
public class SimMqttClient : IAsyncDisposable
{
    private readonly IMqttClient _mqttClient;
    private readonly HeaderIdCounter _headerCounter = new();
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly MqttFactory _factory = new();

    private SimMqttOptions _options;
    private bool _isExplicitDisconnect;
    private bool _disposed;
    private CancellationTokenSource? _reconnectCts;
    private int _reconnectAttempt;

    public SimMqttOptions Options => _options;
    public HeaderIdCounter HeaderCounter => _headerCounter;
    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    // Events
    public event Func<OrderMessage, Task>? OrderReceived;
    public event Func<InstantActionsMessage, Task>? InstantActionsReceived;
    public event Action<ConnectionState>? ConnectionStateChanged;
    public event Func<Task>? Reconnected;
    public event Action<string>? LogMessage;

    public SimMqttClient(SimMqttOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mqttClient = _factory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += HandleIncomingMessageAsync;
        _mqttClient.ConnectedAsync += HandleConnectedAsync;
        _mqttClient.DisconnectedAsync += HandleDisconnectedAsync;
    }

    public void UpdateOptions(SimMqttOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Connects to the MQTT broker per §11.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_mqttClient.IsConnected)
        {
            return;
        }

        _isExplicitDisconnect = false;
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();

        var connectionTopic = Vda5050Topic.Build(_options.Manufacturer, _options.SerialNumber, Vda5050Topic.Connection);

        // Build Last-Will message (§11: CONNECTIONBROKEN, QoS1, Retained)
        var willMsg = new ConnectionMessage
        {
            ConnectionState = ConnectionState.CONNECTIONBROKEN
        };
        // Stamp will message header
        _headerCounter.Stamp(willMsg, Vda5050Topic.Connection, _options.Manufacturer, _options.SerialNumber);
        var willPayload = Vda5050Json.Serialize(willMsg);

        var mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.GetEffectiveClientId())
            .WithCleanSession()
            .WithWillTopic(connectionTopic)
            .WithWillPayload(Encoding.UTF8.GetBytes(willPayload))
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(true)
            .Build();

        Log($"Connecting to broker at {_options.Host}:{_options.Port} as {_options.SerialNumber}...");
        await _mqttClient.ConnectAsync(mqttOptions, cancellationToken);
    }

    /// <summary>
    /// Graceful disconnect per §11: publish OFFLINE (QoS1 retained), then disconnect.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _isExplicitDisconnect = true;
        _reconnectCts?.Cancel();

        if (_mqttClient.IsConnected)
        {
            try
            {
                Log("Graceful shutdown: publishing OFFLINE (QoS1 retained)...");
                var offlineMsg = new ConnectionMessage
                {
                    ConnectionState = ConnectionState.OFFLINE
                };
                await PublishConnectionMessageAsync(offlineMsg, cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"Error publishing OFFLINE on disconnect: {ex.Message}");
            }

            try
            {
                await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"Error during MQTT disconnect: {ex.Message}");
            }
        }

        ConnectionStateChanged?.Invoke(ConnectionState.OFFLINE);
    }

    /// <summary>
    /// Serialized publish method (single-writer per §19, stamps headerId per §17).
    /// </summary>
    public async Task PublishAsync<T>(string topicName, T message, CancellationToken cancellationToken = default) where T : Vda5050Header
    {
        if (!_mqttClient.IsConnected)
        {
            return;
        }

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            // 1. Stamp header
            _headerCounter.Stamp(message, topicName, _options.Manufacturer, _options.SerialNumber);

            // 2. Serialize JSON per §6
            var json = Vda5050Json.Serialize(message);
            var topic = Vda5050Topic.Build(_options.Manufacturer, _options.SerialNumber, topicName);

            // 3. Determine QoS and Retain per §5
            var qos = topicName == Vda5050Topic.Connection ? MqttQualityOfServiceLevel.AtLeastOnce : MqttQualityOfServiceLevel.AtMostOnce;
            var retain = topicName == Vda5050Topic.Connection;

            var mqttMsg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(json)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();

            await _mqttClient.PublishAsync(mqttMsg, cancellationToken);
        }
        catch (Exception ex)
        {
            Log($"Publish error on topic {topicName}: {ex.Message}");
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task PublishConnectionMessageAsync(ConnectionMessage msg, CancellationToken ct = default)
    {
        await _publishLock.WaitAsync(ct);
        try
        {
            _headerCounter.Stamp(msg, Vda5050Topic.Connection, _options.Manufacturer, _options.SerialNumber);
            var json = Vda5050Json.Serialize(msg);
            var topic = Vda5050Topic.Build(_options.Manufacturer, _options.SerialNumber, Vda5050Topic.Connection);

            var mqttMsg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(json)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(true)
                .Build();

            await _mqttClient.PublishAsync(mqttMsg, ct);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task HandleConnectedAsync(MqttClientConnectedEventArgs args)
    {
        Log("MQTT connected successfully.");
        _reconnectAttempt = 0;

        try
        {
            // 1. Subscribe to order and instantActions topics
            var orderTopic = Vda5050Topic.Build(_options.Manufacturer, _options.SerialNumber, Vda5050Topic.Order);
            var instantActionsTopic = Vda5050Topic.Build(_options.Manufacturer, _options.SerialNumber, Vda5050Topic.InstantActions);

            await _mqttClient.SubscribeAsync(orderTopic, MqttQualityOfServiceLevel.AtMostOnce);
            await _mqttClient.SubscribeAsync(instantActionsTopic, MqttQualityOfServiceLevel.AtMostOnce);
            Log($"Subscribed to {orderTopic} and {instantActionsTopic}");

            // 2. Publish ONLINE (QoS1 retained per §11)
            var onlineMsg = new ConnectionMessage
            {
                ConnectionState = ConnectionState.ONLINE
            };
            await PublishConnectionMessageAsync(onlineMsg);
            ConnectionStateChanged?.Invoke(ConnectionState.ONLINE);

            // 3. Notify reconnect / initial connect
            if (Reconnected != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Reconnected.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log($"Error in Reconnected handler: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log($"Error in HandleConnectedAsync: {ex.Message}");
        }
    }

    private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        Log($"MQTT disconnected. Reason: {args.Reason}");
        ConnectionStateChanged?.Invoke(ConnectionState.CONNECTIONBROKEN);

        if (!_isExplicitDisconnect && !_disposed)
        {
            // Trigger exponential backoff reconnect
            _ = Task.Run(ReconnectLoopAsync);
        }

        return Task.CompletedTask;
    }

    private async Task ReconnectLoopAsync()
    {
        var token = _reconnectCts?.Token ?? CancellationToken.None;

        while (!_isExplicitDisconnect && !_disposed && !_mqttClient.IsConnected && !token.IsCancellationRequested)
        {
            _reconnectAttempt++;
            // Exponential backoff: 1s, 2s, 4s, max 10s
            var delayMs = Math.Min(10000, 1000 * (int)Math.Pow(2, Math.Min(4, _reconnectAttempt - 1)));
            Log($"Will attempt reconnect in {delayMs}ms (attempt #{_reconnectAttempt})...");

            try
            {
                await Task.Delay(delayMs, token);
                if (_isExplicitDisconnect || _disposed || token.IsCancellationRequested)
                {
                    break;
                }

                await ConnectAsync(token);
                if (_mqttClient.IsConnected)
                {
                    Log("Reconnected successfully.");
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Reconnect attempt #{_reconnectAttempt} failed: {ex.Message}");
            }
        }
    }

    private async Task HandleIncomingMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            if (!Vda5050Topic.TryParse(topic, out var topicInfo))
            {
                return;
            }

            if (!string.Equals(topicInfo.Manufacturer, _options.Manufacturer, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(topicInfo.SerialNumber, _options.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var payloadBytes = args.ApplicationMessage.PayloadSegment.Array;
            var offset = args.ApplicationMessage.PayloadSegment.Offset;
            var count = args.ApplicationMessage.PayloadSegment.Count;
            if (payloadBytes == null || count == 0)
            {
                return;
            }

            var json = Encoding.UTF8.GetString(payloadBytes, offset, count);

            if (topicInfo.Topic == Vda5050Topic.Order)
            {
                OrderMessage? order = null;
                try
                {
                    order = Vda5050Json.Deserialize<OrderMessage>(json);
                }
                catch (Exception ex)
                {
                    Log($"Failed to parse OrderMessage: {ex.Message}");
                }

                if (order != null && OrderReceived != null)
                {
                    await OrderReceived.Invoke(order);
                }
            }
            else if (topicInfo.Topic == Vda5050Topic.InstantActions)
            {
                InstantActionsMessage? actionsMsg = null;
                try
                {
                    actionsMsg = Vda5050Json.Deserialize<InstantActionsMessage>(json);
                }
                catch (Exception ex)
                {
                    Log($"Failed to parse InstantActionsMessage: {ex.Message}");
                }

                if (actionsMsg != null && InstantActionsReceived != null)
                {
                    await InstantActionsReceived.Invoke(actionsMsg);
                }
            }
        }
        catch (Exception ex)
        {
            // Per §19: MQTT callbacks must never throw unhandled exceptions
            Log($"Unhandled error in HandleIncomingMessageAsync: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [SimMqttClient] {message}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await DisconnectAsync();
        }
        catch
        {
            // ignore
        }

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _publishLock.Dispose();
        _mqttClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
