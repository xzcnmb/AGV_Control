using System.Diagnostics;
using System.Text;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Json;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;
using AgvDispatch.Vda5050.Topics;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace AgvDispatch.Tests;

/// <summary>
/// Headless wire-level integration tests: a real MQTTnet broker plus two real MQTT clients
/// (standing in for Master Control and the AGV Simulator) exercising the transport + Vda5050Json
/// serialization contract both apps depend on. Verifies ONLINE, order dispatch, state round-trip,
/// and the CONNECTIONBROKEN last-will path across the broker.
/// </summary>
public sealed class MqttIntegrationTests : IAsyncLifetime
{
    // Non-standard port to avoid clashing with a real broker / the app's default 1883.
    private const int Port = 18831;
    private const string Manufacturer = "AgvSim";
    private const string Serial = "AGV0001";

    private readonly MqttFactory _factory = new();
    private MqttServer? _broker;

    public async Task InitializeAsync()
    {
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(Port)
            .Build();
        _broker = _factory.CreateMqttServer(options);
        await _broker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_broker != null)
        {
            await _broker.StopAsync();
            _broker.Dispose();
        }
    }

    private async Task<IMqttClient> ConnectClientAsync(
        string clientId,
        MqttApplicationMessage? will = null)
    {
        var client = _factory.CreateMqttClient();
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer("[IP]", Port)
            .WithClientId(clientId)
            .WithCleanSession();
        if (will != null)
        {
            builder = builder
                .WithWillTopic(will.Topic)
                .WithWillPayload(will.PayloadSegment.ToArray())
                .WithWillQualityOfServiceLevel(will.QualityOfServiceLevel)
                .WithWillRetain(will.Retain);
        }
        await client.ConnectAsync(builder.Build());
        return client;
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    private static MqttApplicationMessage Msg<T>(string topic, T payload, bool retain, MqttQualityOfServiceLevel qos)
        => new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(Vda5050Json.Serialize(payload)))
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(retain)
            .Build();

    [Fact]
    public async Task SimulatorOnline_IsReceivedByMaster()
    {
        var master = await ConnectClientAsync("master");
        ConnectionMessage? received = null;
        var connTopic = Vda5050Topic.Build(Manufacturer, Serial, Vda5050Topic.Connection);

        master.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == connTopic)
            {
                var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());
                received = Vda5050Json.Deserialize<ConnectionMessage>(json);
            }
            return Task.CompletedTask;
        };
        await master.SubscribeAsync(Vda5050Topic.InterfaceName + "/" + Vda5050Topic.MajorVersion + "/#");

        var sim = await ConnectClientAsync("sim");
        var online = new ConnectionMessage
        {
            HeaderId = 0,
            Timestamp = DateTime.UtcNow,
            Version = "2.0.0",
            Manufacturer = Manufacturer,
            SerialNumber = Serial,
            ConnectionState = ConnectionState.ONLINE
        };
        await sim.PublishAsync(Msg(connTopic, online, retain: true, MqttQualityOfServiceLevel.AtLeastOnce));

        Assert.True(await WaitForAsync(() => received != null), "Master did not receive the ONLINE connection message.");
        Assert.Equal(ConnectionState.ONLINE, received!.ConnectionState);
        Assert.Equal(Serial, received.SerialNumber);

        await sim.DisconnectAsync();
        await master.DisconnectAsync();
    }

    [Fact]
    public async Task OrderDispatch_RoundTripsToSimulator()
    {
        var orderTopic = Vda5050Topic.Build(Manufacturer, Serial, Vda5050Topic.Order);

        var sim = await ConnectClientAsync("sim");
        OrderMessage? received = null;
        sim.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == orderTopic)
            {
                var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());
                received = Vda5050Json.Deserialize<OrderMessage>(json);
            }
            return Task.CompletedTask;
        };
        await sim.SubscribeAsync(orderTopic);

        var master = await ConnectClientAsync("master");
        var order = new OrderMessage
        {
            HeaderId = 0,
            Timestamp = DateTime.UtcNow,
            Version = "2.0.0",
            Manufacturer = Manufacturer,
            SerialNumber = Serial,
            OrderId = "ORD-1",
            OrderUpdateId = 0,
            Nodes =
            {
                new Node { NodeId = "n1", SequenceId = 0, Released = true,
                    NodePosition = new NodePosition { X = 0, Y = 0, MapId = "default" } },
                new Node { NodeId = "n2", SequenceId = 2, Released = true,
                    NodePosition = new NodePosition { X = 5, Y = 0, MapId = "default" } }
            },
            Edges =
            {
                new Edge { EdgeId = "e1", SequenceId = 1, Released = true, StartNodeId = "n1", EndNodeId = "n2" }
            }
        };
        await master.PublishAsync(Msg(orderTopic, order, retain: false, MqttQualityOfServiceLevel.AtMostOnce));

        Assert.True(await WaitForAsync(() => received != null), "Simulator did not receive the dispatched order.");
        Assert.Equal("ORD-1", received!.OrderId);
        Assert.Equal(2, received.Nodes.Count);
        Assert.Single(received.Edges);
        Assert.Equal("n2", received.Edges[0].EndNodeId);

        await sim.DisconnectAsync();
        await master.DisconnectAsync();
    }

    [Fact]
    public async Task State_RoundTripsToMaster()
    {
        var stateTopic = Vda5050Topic.Build(Manufacturer, Serial, Vda5050Topic.State);

        var master = await ConnectClientAsync("master");
        StateMessage? received = null;
        master.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == stateTopic)
            {
                var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());
                received = Vda5050Json.Deserialize<StateMessage>(json);
            }
            return Task.CompletedTask;
        };
        await master.SubscribeAsync(stateTopic);

        var sim = await ConnectClientAsync("sim");
        var state = new StateMessage
        {
            HeaderId = 0,
            Timestamp = DateTime.UtcNow,
            Version = "2.0.0",
            Manufacturer = Manufacturer,
            SerialNumber = Serial,
            OrderId = "ORD-1",
            Driving = true,
            OperatingMode = OperatingMode.AUTOMATIC,
            BatteryState = new BatteryState { BatteryCharge = 87.5, Charging = false },
            AgvPosition = new AgvPosition { X = 2.5, Y = 0, Theta = 0, MapId = "default", PositionInitialized = true }
        };
        await sim.PublishAsync(Msg(stateTopic, state, retain: false, MqttQualityOfServiceLevel.AtMostOnce));

        Assert.True(await WaitForAsync(() => received != null), "Master did not receive the state message.");
        Assert.Equal("ORD-1", received!.OrderId);
        Assert.True(received.Driving);
        Assert.Equal(87.5, received.BatteryState.BatteryCharge);
        Assert.NotNull(received.AgvPosition);
        Assert.Equal(2.5, received.AgvPosition!.X);

        await sim.DisconnectAsync();
        await master.DisconnectAsync();
    }

    [Fact]
    public async Task UngracefulDisconnect_PublishesConnectionBrokenWill()
    {
        var connTopic = Vda5050Topic.Build(Manufacturer, Serial, Vda5050Topic.Connection);

        var master = await ConnectClientAsync("master");
        ConnectionState? lastState = null;
        master.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == connTopic)
            {
                var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());
                lastState = Vda5050Json.Deserialize<ConnectionMessage>(json)?.ConnectionState;
            }
            return Task.CompletedTask;
        };
        await master.SubscribeAsync(connTopic);

        var willPayload = new ConnectionMessage
        {
            HeaderId = 0,
            Timestamp = DateTime.UtcNow,
            Version = "2.0.0",
            Manufacturer = Manufacturer,
            SerialNumber = Serial,
            ConnectionState = ConnectionState.CONNECTIONBROKEN
        };
        var will = Msg(connTopic, willPayload, retain: true, MqttQualityOfServiceLevel.AtLeastOnce);
        var sim = await ConnectClientAsync("sim-will", will);

        // Force an ungraceful disconnect from the broker side so the will fires.
        await _broker!.DisconnectClientAsync("sim-will", MQTTnet.Protocol.MqttDisconnectReasonCode.ServerShuttingDown);

        Assert.True(await WaitForAsync(() => lastState == ConnectionState.CONNECTIONBROKEN),
            "Broker did not publish the CONNECTIONBROKEN last-will after ungraceful disconnect.");

        await master.DisconnectAsync();
    }
}
