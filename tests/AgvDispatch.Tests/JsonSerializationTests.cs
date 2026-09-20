namespace AgvDispatch.Tests;

using System.Text.Json;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Json;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;

public class JsonSerializationTests
{
    [Theory]
    [InlineData(ConnectionState.ONLINE, "ONLINE")]
    [InlineData(ConnectionState.OFFLINE, "OFFLINE")]
    [InlineData(ConnectionState.CONNECTIONBROKEN, "CONNECTIONBROKEN")]
    public void Enums_ConnectionState_SerializesToExactUppercaseWireValues(ConnectionState state, string expectedWireValue)
    {
        var msg = new ConnectionMessage
        {
            HeaderId = 1,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            ConnectionState = state
        };

        var json = Vda5050Json.Serialize(msg);
        Assert.Contains($"\"connectionState\":\"{expectedWireValue}\"", json);

        var roundTripped = Vda5050Json.Deserialize<ConnectionMessage>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(state, roundTripped.ConnectionState);
    }

    [Theory]
    [InlineData(OperatingMode.AUTOMATIC, "AUTOMATIC")]
    [InlineData(OperatingMode.SEMIAUTOMATIC, "SEMIAUTOMATIC")]
    [InlineData(OperatingMode.MANUAL, "MANUAL")]
    [InlineData(OperatingMode.SERVICE, "SERVICE")]
    [InlineData(OperatingMode.TEACHIN, "TEACHIN")]
    public void Enums_OperatingMode_SerializesToExactUppercaseWireValues(OperatingMode mode, string expectedWireValue)
    {
        var msg = new StateMessage { OperatingMode = mode };
        var json = Vda5050Json.Serialize(msg);

        Assert.Contains($"\"operatingMode\":\"{expectedWireValue}\"", json);

        var roundTripped = Vda5050Json.Deserialize<StateMessage>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(mode, roundTripped.OperatingMode);
    }

    [Theory]
    [InlineData(BlockingType.NONE, "NONE")]
    [InlineData(BlockingType.SOFT, "SOFT")]
    [InlineData(BlockingType.HARD, "HARD")]
    public void Enums_BlockingType_SerializesToExactUppercaseWireValues(BlockingType blockingType, string expectedWireValue)
    {
        var action = new VdaAction
        {
            ActionId = "act-1",
            ActionType = "cancelOrder",
            BlockingType = blockingType
        };

        var json = Vda5050Json.Serialize(action);
        Assert.Contains($"\"blockingType\":\"{expectedWireValue}\"", json);

        var roundTripped = Vda5050Json.Deserialize<VdaAction>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(blockingType, roundTripped.BlockingType);
    }

    [Theory]
    [InlineData(EStop.AUTOACK, "AUTOACK")]
    [InlineData(EStop.MANUAL, "MANUAL")]
    [InlineData(EStop.REMOTE, "REMOTE")]
    [InlineData(EStop.NONE, "NONE")]
    public void Enums_EStop_SerializesToExactUppercaseWireValues(EStop eStop, string expectedWireValue)
    {
        var safetyState = new SafetyState { EStop = eStop, FieldViolation = false };
        var json = Vda5050Json.Serialize(safetyState);

        Assert.Contains($"\"eStop\":\"{expectedWireValue}\"", json);

        var roundTripped = Vda5050Json.Deserialize<SafetyState>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(eStop, roundTripped.EStop);
    }

    [Theory]
    [InlineData(ActionStatus.WAITING, "WAITING")]
    [InlineData(ActionStatus.INITIALIZING, "INITIALIZING")]
    [InlineData(ActionStatus.RUNNING, "RUNNING")]
    [InlineData(ActionStatus.PAUSED, "PAUSED")]
    [InlineData(ActionStatus.FINISHED, "FINISHED")]
    [InlineData(ActionStatus.FAILED, "FAILED")]
    public void Enums_ActionStatus_SerializesToExactUppercaseWireValues(ActionStatus status, string expectedWireValue)
    {
        var actionState = new ActionState
        {
            ActionId = "act-1",
            ActionStatus = status
        };

        var json = Vda5050Json.Serialize(actionState);
        Assert.Contains($"\"actionStatus\":\"{expectedWireValue}\"", json);

        var roundTripped = Vda5050Json.Deserialize<ActionState>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(status, roundTripped.ActionStatus);
    }

    [Theory]
    [InlineData(ErrorLevel.WARNING, "WARNING")]
    [InlineData(ErrorLevel.FATAL, "FATAL")]
    public void Enums_ErrorLevel_SerializesToExactUppercaseWireValues(ErrorLevel level, string expectedWireValue)
    {
        var error = new Error { ErrorType = "ERR", ErrorLevel = level };
        var json = Vda5050Json.Serialize(error);

        Assert.Contains($"\"errorLevel\":\"{expectedWireValue}\"", json);
    }

    [Theory]
    [InlineData(InfoLevel.INFO, "INFO")]
    [InlineData(InfoLevel.DEBUG, "DEBUG")]
    public void Enums_InfoLevel_SerializesToExactUppercaseWireValues(InfoLevel level, string expectedWireValue)
    {
        var info = new Info { InfoType = "INF", InfoLevel = level };
        var json = Vda5050Json.Serialize(info);

        Assert.Contains($"\"infoLevel\":\"{expectedWireValue}\"", json);
    }

    [Fact]
    public void Timestamp_SerializesToExactIso8601UtcWithMilliseconds()
    {
        var specificUtc = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
        var msg = new StateMessage
        {
            Timestamp = specificUtc,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001"
        };

        var json = Vda5050Json.Serialize(msg);
        Assert.Contains("\"timestamp\":\"2026-01-02T03:04:05.678Z\"", json);

        var deserialized = Vda5050Json.Deserialize<StateMessage>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(specificUtc, deserialized.Timestamp);
    }

    [Fact]
    public void RequiredEmptyArrays_AreSerializedInStateMessage()
    {
        var msg = new StateMessage
        {
            HeaderId = 1,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            NodeStates = new List<NodeState>(),
            EdgeStates = new List<EdgeState>(),
            ActionStates = new List<ActionState>(),
            Errors = new List<Error>(),
            Information = new List<Info>()
        };

        var json = Vda5050Json.Serialize(msg);

        Assert.Contains("\"nodeStates\":[]", json);
        Assert.Contains("\"edgeStates\":[]", json);
        Assert.Contains("\"actionStates\":[]", json);
        Assert.Contains("\"errors\":[]", json);
        Assert.Contains("\"information\":[]", json);
    }

    [Fact]
    public void OptionalNullFields_AreOmittedFromStateMessageJson()
    {
        var msg = new StateMessage
        {
            HeaderId = 1,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            AgvPosition = null,
            Velocity = null,
            Loads = null,
            Paused = null,
            NewBaseRequest = null,
            DistanceSinceLastNode = null,
            ZoneSetId = null
        };

        var json = Vda5050Json.Serialize(msg);

        Assert.DoesNotContain("\"agvPosition\"", json);
        Assert.DoesNotContain("\"velocity\"", json);
        Assert.DoesNotContain("\"loads\"", json);
        Assert.DoesNotContain("\"paused\"", json);
        Assert.DoesNotContain("\"newBaseRequest\"", json);
        Assert.DoesNotContain("\"distanceSinceLastNode\"", json);
        Assert.DoesNotContain("\"zoneSetId\"", json);
    }

    [Fact]
    public void CamelCasePropertyNames_FollowExactCasingPerContract()
    {
        var state = Vda5050TestFactory.CreatePopulatedStateMessage();
        var json = Vda5050Json.Serialize(state);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("orderId", out _), "Missing camelCase property 'orderId'");
        Assert.True(root.TryGetProperty("orderUpdateId", out _), "Missing camelCase property 'orderUpdateId'");
        Assert.True(root.TryGetProperty("lastNodeId", out _), "Missing camelCase property 'lastNodeId'");
        Assert.True(root.TryGetProperty("lastNodeSequenceId", out _), "Missing camelCase property 'lastNodeSequenceId'");
        Assert.True(root.TryGetProperty("nodeStates", out var nodeStatesEl), "Missing camelCase property 'nodeStates'");
        Assert.True(root.TryGetProperty("agvPosition", out _), "Missing exact camelCase property 'agvPosition'");
        Assert.True(root.TryGetProperty("batteryState", out var batteryEl), "Missing camelCase property 'batteryState'");
        Assert.True(root.TryGetProperty("safetyState", out var safetyEl), "Missing camelCase property 'safetyState'");

        Assert.True(batteryEl.TryGetProperty("batteryCharge", out _), "Missing camelCase property 'batteryCharge'");
        Assert.True(safetyEl.TryGetProperty("eStop", out _), "Missing exact property 'eStop'");

        var firstNodeState = nodeStatesEl[0];
        Assert.True(firstNodeState.TryGetProperty("sequenceId", out _), "Missing camelCase property 'sequenceId'");
    }

    [Fact]
    public void ErrorReferences_SerializesAsArrayOfObjectsWithReferenceKeyAndValue()
    {
        var error = new Error
        {
            ErrorType = "ORDER_VALIDATION_ERROR",
            ErrorLevel = ErrorLevel.WARNING,
            ErrorDescription = "Node validation failed",
            ErrorReferences = new List<ErrorReference>
            {
                new("nodeId", "node_3"),
                new("sequenceId", "6")
            }
        };

        var json = Vda5050Json.Serialize(error);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("errorReferences", out var refs));
        Assert.Equal(JsonValueKind.Array, refs.ValueKind);
        Assert.Equal(2, refs.GetArrayLength());

        var first = refs[0];
        Assert.Equal(JsonValueKind.Object, first.ValueKind);
        Assert.Equal("nodeId", first.GetProperty("referenceKey").GetString());
        Assert.Equal("node_3", first.GetProperty("referenceValue").GetString());
    }

    [Fact]
    public void FullyPopulatedOrderMessage_RoundTripsSuccessfully()
    {
        var order = new OrderMessage
        {
            HeaderId = 10,
            Timestamp = new DateTime(2026, 6, 15, 12, 0, 0, 123, DateTimeKind.Utc),
            Version = "2.0.0",
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            OrderId = "ord-999",
            OrderUpdateId = 2,
            ZoneSetId = "zone-B",
            Nodes = new List<Node>
            {
                new()
                {
                    NodeId = "node_0",
                    SequenceId = 0,
                    Released = true,
                    NodeDescription = "Pick station",
                    NodePosition = new NodePosition
                    {
                        X = 1.0,
                        Y = 2.0,
                        Theta = 3.14,
                        MapId = "default",
                        AllowedDeviationXY = 0.05,
                        AllowedDeviationTheta = 0.01
                    },
                    Actions = new List<VdaAction>
                    {
                        new()
                        {
                            ActionId = "act-pick",
                            ActionType = "pick",
                            BlockingType = BlockingType.HARD,
                            ActionParameters = new List<ActionParameter>
                            {
                                new("station", "ST-1"),
                                new("height", 120)
                            }
                        }
                    }
                },
                new()
                {
                    NodeId = "node_1",
                    SequenceId = 2,
                    Released = true
                }
            },
            Edges = new List<Edge>
            {
                new()
                {
                    EdgeId = "edge_0",
                    SequenceId = 1,
                    Released = true,
                    StartNodeId = "node_0",
                    EndNodeId = "node_1",
                    MaxSpeed = 1.5,
                    Trajectory = new Trajectory
                    {
                        Degree = 1,
                        KnotVector = new List<double> { 0.0, 1.0 },
                        ControlPoints = new List<ControlPoint>
                        {
                            new() { X = 1.0, Y = 2.0, Weight = 1.0 },
                            new() { X = 5.0, Y = 2.0, Weight = 1.0 }
                        }
                    }
                }
            }
        };

        var json = Vda5050Json.Serialize(order);
        var roundTripped = Vda5050Json.Deserialize<OrderMessage>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(order.OrderId, roundTripped.OrderId);
        Assert.Equal(order.OrderUpdateId, roundTripped.OrderUpdateId);
        Assert.Equal(order.ZoneSetId, roundTripped.ZoneSetId);
        Assert.Equal(order.Nodes.Count, roundTripped.Nodes.Count);
        Assert.Equal(order.Edges.Count, roundTripped.Edges.Count);

        var n0 = roundTripped.Nodes[0];
        Assert.Equal("node_0", n0.NodeId);
        Assert.True(n0.Released);
        Assert.NotNull(n0.NodePosition);
        Assert.Equal(1.0, n0.NodePosition.X);
        Assert.Single(n0.Actions);
        Assert.Equal("pick", n0.Actions[0].ActionType);
        Assert.Equal(BlockingType.HARD, n0.Actions[0].BlockingType);

        var e0 = roundTripped.Edges[0];
        Assert.Equal("edge_0", e0.EdgeId);
        Assert.Equal("node_0", e0.StartNodeId);
        Assert.Equal("node_1", e0.EndNodeId);
        Assert.NotNull(e0.Trajectory);
        Assert.Equal(2, e0.Trajectory.ControlPoints.Count);
    }

    [Fact]
    public void FullyPopulatedStateMessage_RoundTripsSuccessfully()
    {
        var state = Vda5050TestFactory.CreatePopulatedStateMessage();
        var json = Vda5050Json.Serialize(state);

        var deserialized = Vda5050Json.Deserialize<StateMessage>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(state.HeaderId, deserialized.HeaderId);
        Assert.Equal(state.Timestamp, deserialized.Timestamp);
        Assert.Equal(state.OrderId, deserialized.OrderId);
        Assert.Equal(state.OrderUpdateId, deserialized.OrderUpdateId);
        Assert.Equal(state.LastNodeId, deserialized.LastNodeId);
        Assert.Equal(state.LastNodeSequenceId, deserialized.LastNodeSequenceId);
        Assert.True(deserialized.Driving);
        Assert.Equal(OperatingMode.AUTOMATIC, deserialized.OperatingMode);

        Assert.NotNull(deserialized.AgvPosition);
        Assert.Equal(state.AgvPosition!.X, deserialized.AgvPosition.X);
        Assert.Equal(state.AgvPosition.Y, deserialized.AgvPosition.Y);
        Assert.Equal("default", deserialized.AgvPosition.MapId);

        Assert.NotNull(deserialized.Velocity);
        Assert.Equal(1.2, deserialized.Velocity.Vx);

        Assert.NotNull(deserialized.BatteryState);
        Assert.Equal(85.5, deserialized.BatteryState.BatteryCharge);

        Assert.NotNull(deserialized.SafetyState);
        Assert.Equal(EStop.NONE, deserialized.SafetyState.EStop);

        Assert.Single(deserialized.Errors);
        Assert.Equal("NO_ORDER_TO_CANCEL", deserialized.Errors[0].ErrorType);
        Assert.Equal(2, deserialized.Errors[0].ErrorReferences?.Count);
        Assert.Equal("actionId", deserialized.Errors[0].ErrorReferences?[0].ReferenceKey);
        Assert.Equal("act-01", deserialized.Errors[0].ErrorReferences?[0].ReferenceValue);

        Assert.Single(deserialized.ActionStates);
        Assert.Equal(ActionStatus.RUNNING, deserialized.ActionStates[0].ActionStatus);

        Assert.Single(deserialized.Loads!);
        Assert.Equal("pallet-99", deserialized.Loads![0].LoadId);
    }
}
