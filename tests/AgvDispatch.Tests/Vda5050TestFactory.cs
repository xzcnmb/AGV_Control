namespace AgvDispatch.Tests;

using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;

/// <summary>
/// Factory helpers for building VDA5050 test fixtures and sample messages.
/// </summary>
public static class Vda5050TestFactory
{
    public static OrderMessage CreateMinimalOrder(string orderId = "order-001", uint orderUpdateId = 0)
    {
        return new OrderMessage
        {
            HeaderId = 1,
            Timestamp = DateTime.UtcNow,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            OrderId = orderId,
            OrderUpdateId = orderUpdateId,
            Nodes = new List<Node>
            {
                new() { NodeId = "node_0", SequenceId = 0, Released = true }
            },
            Edges = new List<Edge>()
        };
    }

    public static OrderMessage CreateLinearOrder(
        int nodeCount = 3,
        int releasedNodeCount = 3,
        string orderId = "order-001",
        uint orderUpdateId = 0)
    {
        var order = new OrderMessage
        {
            HeaderId = 1,
            Timestamp = DateTime.UtcNow,
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            OrderId = orderId,
            OrderUpdateId = orderUpdateId
        };

        for (int i = 0; i < nodeCount; i++)
        {
            bool nodeReleased = i < releasedNodeCount;
            order.Nodes.Add(new Node
            {
                NodeId = $"node_{i}",
                SequenceId = (uint)(i * 2),
                Released = nodeReleased,
                NodePosition = new NodePosition
                {
                    X = i * 2.0,
                    Y = 0.0,
                    Theta = 0.0,
                    MapId = "default"
                }
            });

            if (i < nodeCount - 1)
            {
                bool edgeReleased = (i < releasedNodeCount) && (i + 1 < releasedNodeCount);
                order.Edges.Add(new Edge
                {
                    EdgeId = $"edge_{i}",
                    SequenceId = (uint)(i * 2 + 1),
                    StartNodeId = $"node_{i}",
                    EndNodeId = $"node_{i + 1}",
                    Released = edgeReleased
                });
            }
        }

        return order;
    }

    public static StateMessage CreatePopulatedStateMessage()
    {
        return new StateMessage
        {
            HeaderId = 42,
            Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc),
            Version = "2.0.0",
            Manufacturer = "AgvSim",
            SerialNumber = "AGV0001",
            OrderId = "order-123",
            OrderUpdateId = 1,
            LastNodeId = "node_1",
            LastNodeSequenceId = 2,
            Driving = true,
            OperatingMode = OperatingMode.AUTOMATIC,
            NodeStates = new List<NodeState>
            {
                new()
                {
                    NodeId = "node_1",
                    SequenceId = 2,
                    Released = true,
                    NodePosition = new NodePosition { X = 10.5, Y = 20.25, Theta = 1.57, MapId = "default" }
                }
            },
            EdgeStates = new List<EdgeState>
            {
                new()
                {
                    EdgeId = "edge_1",
                    SequenceId = 3,
                    Released = true
                }
            },
            ActionStates = new List<ActionState>
            {
                new()
                {
                    ActionId = "act-01",
                    ActionType = "cancelOrder",
                    ActionStatus = ActionStatus.RUNNING,
                    ActionDescription = "Cancel order in progress"
                }
            },
            BatteryState = new BatteryState
            {
                BatteryCharge = 85.5,
                Charging = false,
                BatteryVoltage = 48.0,
                BatteryHealth = 99.0,
                Reach = 1200.0
            },
            SafetyState = new SafetyState
            {
                EStop = EStop.NONE,
                FieldViolation = false
            },
            Errors = new List<Error>
            {
                new()
                {
                    ErrorType = "NO_ORDER_TO_CANCEL",
                    ErrorLevel = ErrorLevel.WARNING,
                    ErrorDescription = "No order currently active to cancel",
                    ErrorReferences = new List<ErrorReference>
                    {
                        new("actionId", "act-01"),
                        new("orderId", "order-123")
                    }
                }
            },
            Information = new List<Info>
            {
                new()
                {
                    InfoType = "NAVIGATION",
                    InfoLevel = InfoLevel.INFO,
                    InfoDescription = "Route recalibrated",
                    InfoReferences = new List<InfoReference>
                    {
                        new("referenceKey", "val")
                    }
                }
            },
            AgvPosition = new AgvPosition
            {
                X = 12.34,
                Y = 56.78,
                Theta = 0.5,
                MapId = "default",
                PositionInitialized = true,
                LocalizationScore = 0.98,
                DeviationRange = 0.05
            },
            Velocity = new Velocity
            {
                Vx = 1.2,
                Vy = 0.0,
                Omega = 0.1
            },
            Loads = new List<Load>
            {
                new()
                {
                    LoadId = "pallet-99",
                    LoadType = "EURO_PALLET",
                    LoadPosition = "top",
                    Weight = 350.0
                }
            },
            Paused = false,
            NewBaseRequest = false,
            DistanceSinceLastNode = 1.5,
            ZoneSetId = "zone-A"
        };
    }
}
