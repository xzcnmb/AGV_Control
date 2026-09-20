using System.Collections.Concurrent;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Json;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;
using AgvDispatch.Vda5050.StateMachines;
using AgvDispatch.Vda5050.Topics;

namespace AgvDispatch.MasterControl.Services;

public class TrackedOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string AgvSerial { get; set; } = string.Empty;
    public uint OrderUpdateId { get; set; }
    public OrderCompletionStatus Status { get; set; } = OrderCompletionStatus.CREATED;

    // The complete route plan
    public List<Node> FullNodes { get; set; } = new();
    public List<Edge> FullEdges { get; set; } = new();

    // Horizon configuration
    public int BaseSize { get; set; } = 2; // Initial released node count (base >= 2)
    public int ReleasedNodeCount { get; set; }

    // Last dispatched message for resending (§4)
    public OrderMessage? LastDispatchedMessage { get; set; }
    public int DispatchAttemptsLeft { get; set; } = 2; // 1 initial + 2 retries = 3 tries
    public System.Timers.Timer? AckTimer { get; set; }

    // Cancel tracking (§13)
    public string? PendingCancelActionId { get; set; }
    public System.Timers.Timer? CancelTimer { get; set; }
}

/// <summary>
/// Service managing order authoring, validation, horizon dispatching (§12b),
/// dispatch ack/resend (§4), instant actions (§10, §13), and order lifecycle FSM (§16).
/// </summary>
public class OrderService : IDisposable
{
    private readonly MqttClientService _mqttClient;
    private readonly AgvRegistry _agvRegistry;
    private readonly TaskRepository _repository;
    private readonly ConcurrentDictionary<string, TrackedOrder> _activeOrdersBySerial = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TrackedOrder> _ordersById = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _orderLock = new();
    private bool _disposed;

    public event Action<string, string>? NotificationRaised; // level, message
    public event Action<TrackedOrder>? OrderStatusChanged;

    public OrderService(MqttClientService mqttClient, AgvRegistry agvRegistry, TaskRepository repository)
    {
        _mqttClient = mqttClient;
        _agvRegistry = agvRegistry;
        _repository = repository;

        _mqttClient.StateReceived += OnStateReceived;
    }

    /// <summary>
    /// Builds a full order from waypoints, assigning sequenceIds (nodes even, edges odd interleaved).
    /// </summary>
    public static (OrderMessage order, ValidationResult validation) CreateLinearOrder(
        string orderId,
        string manufacturer,
        string serialNumber,
        List<(string nodeId, double x, double y)> waypoints,
        int baseSize = 2)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            orderId = "ORD-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Random.Shared.Next(1000, 9999);
        }

        var order = new OrderMessage
        {
            OrderId = orderId,
            OrderUpdateId = 0,
            Manufacturer = manufacturer,
            SerialNumber = serialNumber,
            Nodes = new List<Node>(),
            Edges = new List<Edge>()
        };

        if (waypoints.Count == 0)
        {
            var emptyRes = new ValidationResult();
            emptyRes.AddError("Order must contain at least one node.");
            return (order, emptyRes);
        }

        uint seq = 0;
        int releaseCount = Math.Max(2, baseSize);

        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            bool isNodeReleased = i < releaseCount;

            var node = new Node
            {
                NodeId = wp.nodeId,
                SequenceId = seq,
                Released = isNodeReleased,
                NodePosition = new NodePosition
                {
                    X = wp.x,
                    Y = wp.y,
                    MapId = "default"
                }
            };
            order.Nodes.Add(node);
            seq++;

            if (i < waypoints.Count - 1)
            {
                var nextWp = waypoints[i + 1];
                bool isEdgeReleased = (i + 1) < releaseCount;

                var edge = new Edge
                {
                    EdgeId = $"edge_{wp.nodeId}_{nextWp.nodeId}",
                    SequenceId = seq,
                    Released = isEdgeReleased,
                    StartNodeId = wp.nodeId,
                    EndNodeId = nextWp.nodeId
                };
                order.Edges.Add(edge);
                seq++;
            }
        }

        var validation = SequenceValidator.Validate(order);
        return (order, validation);
    }

    /// <summary>
    /// Dispatches an order with horizon model (§12b).
    /// Refuses dispatch to non-Online or FATAL AGVs (§15).
    /// </summary>
    public async Task<(bool success, string error)> DispatchOrderAsync(OrderMessage order, int baseSize = 2)
    {
        var validation = SequenceValidator.Validate(order);
        if (!validation.IsValid)
        {
            return (false, "Validation failed: " + string.Join("; ", validation.Errors));
        }

        var agv = _agvRegistry.GetAgv(order.SerialNumber);
        if (agv == null || agv.ConnectionState != MasterConnectionState.Online)
        {
            return (false, $"AGV '{order.SerialNumber}' is not online (State: {agv?.ConnectionState ?? MasterConnectionState.Unknown}). Dispatch refused.");
        }

        if (agv.HasFatalError)
        {
            return (false, $"AGV '{order.SerialNumber}' is in FATAL error state. Dispatch refused (§15).");
        }

        TrackedOrder tracked;
        lock (_orderLock)
        {
            if (_activeOrdersBySerial.TryGetValue(order.SerialNumber, out var existingActive)
                && existingActive.Status != OrderCompletionStatus.COMPLETED
                && existingActive.Status != OrderCompletionStatus.CANCELLED
                && existingActive.Status != OrderCompletionStatus.FAILED)
            {
                return (false, $"AGV '{order.SerialNumber}' already has active order '{existingActive.OrderId}'. Cancel it before dispatching a new order.");
            }

            int releasedCount = order.Nodes.Count(n => n.Released);
            tracked = new TrackedOrder
            {
                OrderId = order.OrderId,
                Manufacturer = order.Manufacturer,
                AgvSerial = order.SerialNumber,
                OrderUpdateId = order.OrderUpdateId,
                FullNodes = new List<Node>(order.Nodes),
                FullEdges = new List<Edge>(order.Edges),
                BaseSize = baseSize,
                ReleasedNodeCount = releasedCount,
                Status = OrderCompletionStatus.CREATED,
                LastDispatchedMessage = order,
                DispatchAttemptsLeft = 2
            };

            _activeOrdersBySerial[order.SerialNumber] = tracked;
            _ordersById[order.OrderId] = tracked;
        }

        // Record CREATED in database (§18)
        var record = new OrderRecord
        {
            OrderId = order.OrderId,
            AgvSerial = order.SerialNumber,
            OrderUpdateId = order.OrderUpdateId,
            Status = OrderCompletionStatus.CREATED.ToString(),
            PayloadJson = Vda5050Json.Serialize(order),
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        _repository.InsertOrder(record);
        _repository.InsertOrderEvent(order.OrderId, order.SerialNumber, "CREATED", record.PayloadJson);
        OrderStatusChanged?.Invoke(tracked);

        // Publish to MQTT
        await PublishAndStartAckTimerAsync(tracked, order);
        return (true, string.Empty);
    }

    private async Task PublishAndStartAckTimerAsync(TrackedOrder tracked, OrderMessage msg)
    {
        lock (_orderLock)
        {
            tracked.LastDispatchedMessage = msg;
            tracked.OrderUpdateId = msg.OrderUpdateId;
            tracked.Status = OrderCompletionStatus.DISPATCHED;

            // Stop existing timer
            tracked.AckTimer?.Stop();
            tracked.AckTimer?.Dispose();

            // 10s Ack timer (§4)
            var timer = new System.Timers.Timer(10000);
            timer.AutoReset = false;
            timer.Elapsed += async (s, e) =>
            {
                try
                {
                    await OnDispatchAckTimeoutAsync(tracked);
                }
                catch (Exception ex)
                {
                    NotificationRaised?.Invoke("ERROR", $"Ack timer error: {ex.Message}");
                }
            };
            tracked.AckTimer = timer;
            timer.Start();
        }

        var nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        _repository.UpdateOrderDispatched(tracked.OrderId, nowIso);
        _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "DISPATCHED", Vda5050Json.Serialize(msg));
        OrderStatusChanged?.Invoke(tracked);

        await _mqttClient.PublishAsync(msg.Manufacturer, msg.SerialNumber, Vda5050Topic.Order, msg, qos: 0, retain: false);
    }

    private async Task OnDispatchAckTimeoutAsync(TrackedOrder tracked)
    {
        bool shouldResend = false;
        OrderMessage? messageToResend = null;

        lock (_orderLock)
        {
            // Only act if order is still in DISPATCHED state waiting for ack
            if (tracked.Status == OrderCompletionStatus.DISPATCHED)
            {
                if (tracked.DispatchAttemptsLeft > 0)
                {
                    tracked.DispatchAttemptsLeft--;
                    shouldResend = true;
                    messageToResend = tracked.LastDispatchedMessage;

                    // Restart timer for next 10s
                    tracked.AckTimer?.Start();
                }
                else
                {
                    // 3 attempts reached (§4), escalate to visible error
                    NotificationRaised?.Invoke("ERROR", $"Dispatch ack timeout for order '{tracked.OrderId}' (updateId {tracked.OrderUpdateId}) on AGV '{tracked.AgvSerial}' after 3 attempts.");
                    _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "DISPATCH_ACK_TIMEOUT", "{\"error\":\"Timeout waiting for state ack after 3 attempts\"}");
                }
            }
        }

        if (shouldResend && messageToResend != null)
        {
            NotificationRaised?.Invoke("WARNING", $"Resending order '{tracked.OrderId}' (updateId {tracked.OrderUpdateId}) to '{tracked.AgvSerial}' (attempts left: {tracked.DispatchAttemptsLeft}).");
            _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "DISPATCH_RESEND", Vda5050Json.Serialize(messageToResend));
            await _mqttClient.PublishAsync(messageToResend.Manufacturer, messageToResend.SerialNumber, Vda5050Topic.Order, messageToResend, qos: 0, retain: false);
        }
    }

    private void OnStateReceived(StateMessage state)
    {
        if (string.IsNullOrEmpty(state.SerialNumber)) return;

        TrackedOrder? tracked = null;
        lock (_orderLock)
        {
            _activeOrdersBySerial.TryGetValue(state.SerialNumber, out tracked);
        }

        if (tracked == null) return;

        // Check if state belongs to tracked order
        if (!string.Equals(state.OrderId, tracked.OrderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_orderLock)
        {
            // 1. Dispatch Ack check (§4)
            if (tracked.Status == OrderCompletionStatus.DISPATCHED && state.OrderUpdateId == tracked.OrderUpdateId)
            {
                tracked.Status = OrderCompletionStatus.ACCEPTED;
                tracked.AckTimer?.Stop();
                tracked.AckTimer?.Dispose();
                tracked.AckTimer = null;

                _repository.UpdateOrderStatus(tracked.OrderId, OrderCompletionStatus.ACCEPTED.ToString());
                _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "ACCEPTED", Vda5050Json.Serialize(state));
                OrderStatusChanged?.Invoke(tracked);
            }

            // 2. Running check (§16)
            if (tracked.Status == OrderCompletionStatus.ACCEPTED)
            {
                if (state.Driving || state.LastNodeSequenceId > 0)
                {
                    tracked.Status = OrderCompletionStatus.RUNNING;
                    _repository.UpdateOrderStatus(tracked.OrderId, OrderCompletionStatus.RUNNING.ToString());
                    _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "RUNNING", Vda5050Json.Serialize(state));
                    OrderStatusChanged?.Invoke(tracked);
                }
            }

            // 3. Cancel check (§13)
            if (tracked.PendingCancelActionId != null)
            {
                var cancelAction = state.ActionStates?.FirstOrDefault(a => a.ActionId == tracked.PendingCancelActionId);
                bool actionsFinished = cancelAction?.ActionStatus == ActionStatus.FINISHED;
                bool statesEmpty = (state.NodeStates == null || state.NodeStates.Count == 0) &&
                                   (state.EdgeStates == null || state.EdgeStates.Count == 0);

                if (actionsFinished && statesEmpty)
                {
                    tracked.CancelTimer?.Stop();
                    tracked.CancelTimer?.Dispose();
                    tracked.CancelTimer = null;
                    tracked.PendingCancelActionId = null;

                    tracked.Status = OrderCompletionStatus.CANCELLED;
                    _repository.UpdateOrderStatus(tracked.OrderId, OrderCompletionStatus.CANCELLED.ToString());
                    _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "CANCELLED", Vda5050Json.Serialize(state));
                    _activeOrdersBySerial.TryRemove(tracked.AgvSerial, out _);
                    OrderStatusChanged?.Invoke(tracked);
                    return;
                }
            }

            // 4. Failed check (§15, §16)
            if (tracked.Status == OrderCompletionStatus.RUNNING || tracked.Status == OrderCompletionStatus.ACCEPTED)
            {
                if (state.Errors != null && state.Errors.Any(e => e.ErrorLevel == ErrorLevel.FATAL))
                {
                    tracked.Status = OrderCompletionStatus.FAILED;
                    _repository.UpdateOrderStatus(tracked.OrderId, OrderCompletionStatus.FAILED.ToString());
                    _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "FAILED", Vda5050Json.Serialize(state));
                    _activeOrdersBySerial.TryRemove(tracked.AgvSerial, out _);
                    OrderStatusChanged?.Invoke(tracked);
                    return;
                }
            }

            // 5. Horizon stitching check: NewBaseRequest == true (§12b)
            if ((tracked.Status == OrderCompletionStatus.RUNNING || tracked.Status == OrderCompletionStatus.ACCEPTED)
                && state.NewBaseRequest == true
                && tracked.ReleasedNodeCount < tracked.FullNodes.Count)
            {
                // Trigger horizon advance
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await AdvanceHorizonAsync(tracked);
                    }
                    catch (Exception ex)
                    {
                        NotificationRaised?.Invoke("ERROR", $"Error advancing horizon for order '{tracked.OrderId}': {ex.Message}");
                    }
                });
            }

            // 6. Completion check (§16):
            // nodeStates & edgeStates empty, driving == false, and all nodes were released
            if (tracked.Status == OrderCompletionStatus.RUNNING)
            {
                bool nodesEmpty = state.NodeStates == null || state.NodeStates.Count == 0;
                bool edgesEmpty = state.EdgeStates == null || state.EdgeStates.Count == 0;
                bool allReleased = tracked.ReleasedNodeCount >= tracked.FullNodes.Count;

                var lastNode = tracked.FullNodes.LastOrDefault();
                bool reachedLastNode = lastNode != null &&
                    string.Equals(state.LastNodeId, lastNode.NodeId, StringComparison.OrdinalIgnoreCase) &&
                    state.LastNodeSequenceId == lastNode.SequenceId;

                if (nodesEmpty && edgesEmpty && !state.Driving && (allReleased || reachedLastNode))
                {
                    tracked.Status = OrderCompletionStatus.COMPLETED;
                    _repository.UpdateOrderStatus(tracked.OrderId, OrderCompletionStatus.COMPLETED.ToString());
                    _repository.InsertOrderEvent(tracked.OrderId, tracked.AgvSerial, "COMPLETED", Vda5050Json.Serialize(state));
                    _activeOrdersBySerial.TryRemove(tracked.AgvSerial, out _);
                    OrderStatusChanged?.Invoke(tracked);
                }
            }
        }
    }

    /// <summary>
    /// Horizon update: flips next batch of nodes/edges to released, preserving prefix and stitching node (§12b).
    /// </summary>
    private async Task AdvanceHorizonAsync(TrackedOrder tracked)
    {
        OrderMessage updateMsg;
        lock (_orderLock)
        {
            if (tracked.ReleasedNodeCount >= tracked.FullNodes.Count) return;

            int stitchIndex = tracked.ReleasedNodeCount - 1;
            if (stitchIndex < 0 || stitchIndex >= tracked.FullNodes.Count) return;

            // Advance release count by baseSize
            int nextReleaseIndex = Math.Min(stitchIndex + tracked.BaseSize, tracked.FullNodes.Count - 1);
            int newlyReleasedCount = nextReleaseIndex + 1;
            tracked.ReleasedNodeCount = newlyReleasedCount;

            var stitchNode = tracked.FullNodes[stitchIndex];

            // Build update order starting from stitch node
            var updateNodes = new List<Node>();
            var updateEdges = new List<Edge>();

            for (int i = stitchIndex; i < tracked.FullNodes.Count; i++)
            {
                var origNode = tracked.FullNodes[i];
                bool isRel = i <= nextReleaseIndex;

                updateNodes.Add(new Node
                {
                    NodeId = origNode.NodeId,
                    SequenceId = origNode.SequenceId,
                    Released = isRel,
                    NodePosition = origNode.NodePosition,
                    Actions = origNode.Actions
                });

                if (i < tracked.FullEdges.Count)
                {
                    var origEdge = tracked.FullEdges[i];
                    bool isEdgeRel = (i + 1) <= nextReleaseIndex;

                    updateEdges.Add(new Edge
                    {
                        EdgeId = origEdge.EdgeId,
                        SequenceId = origEdge.SequenceId,
                        Released = isEdgeRel,
                        StartNodeId = origEdge.StartNodeId,
                        EndNodeId = origEdge.EndNodeId,
                        MaxSpeed = origEdge.MaxSpeed,
                        Orientation = origEdge.Orientation,
                        Trajectory = origEdge.Trajectory,
                        Actions = origEdge.Actions
                    });
                }
            }

            tracked.OrderUpdateId++;

            updateMsg = new OrderMessage
            {
                OrderId = tracked.OrderId,
                OrderUpdateId = tracked.OrderUpdateId,
                Manufacturer = tracked.Manufacturer,
                SerialNumber = tracked.AgvSerial,
                Nodes = updateNodes,
                Edges = updateEdges
            };

            var val = SequenceValidator.Validate(updateMsg);
            if (!val.IsValid)
            {
                NotificationRaised?.Invoke("ERROR", $"Horizon update validation failed: {string.Join("; ", val.Errors)}");
                return;
            }

            tracked.DispatchAttemptsLeft = 2;
        }

        NotificationRaised?.Invoke("INFO", $"Sending horizon update for order '{tracked.OrderId}' updateId={updateMsg.OrderUpdateId} (nodes released: {tracked.ReleasedNodeCount}/{tracked.FullNodes.Count}).");
        await PublishAndStartAckTimerAsync(tracked, updateMsg);
    }

    #region Instant Actions (§10, §13, §14)

    public async Task<bool> SendStartPauseAsync(string manufacturer, string serialNumber)
    {
        var action = new VdaAction
        {
            ActionId = Guid.NewGuid().ToString("N"),
            ActionType = "startPause",
            BlockingType = BlockingType.HARD,
            ActionDescription = "Pause AGV"
        };
        return await SendInstantActionAsync(manufacturer, serialNumber, action);
    }

    public async Task<bool> SendStopPauseAsync(string manufacturer, string serialNumber)
    {
        var action = new VdaAction
        {
            ActionId = Guid.NewGuid().ToString("N"),
            ActionType = "stopPause",
            BlockingType = BlockingType.HARD,
            ActionDescription = "Resume AGV"
        };
        return await SendInstantActionAsync(manufacturer, serialNumber, action);
    }

    public async Task<bool> SendFactsheetRequestAsync(string manufacturer, string serialNumber)
    {
        var action = new VdaAction
        {
            ActionId = Guid.NewGuid().ToString("N"),
            ActionType = "factsheetRequest",
            BlockingType = BlockingType.NONE,
            ActionDescription = "Request Factsheet"
        };
        return await SendInstantActionAsync(manufacturer, serialNumber, action);
    }

    public async Task<bool> SendStateRequestAsync(string manufacturer, string serialNumber)
    {
        var action = new VdaAction
        {
            ActionId = Guid.NewGuid().ToString("N"),
            ActionType = "stateRequest",
            BlockingType = BlockingType.NONE,
            ActionDescription = "Request State"
        };
        return await SendInstantActionAsync(manufacturer, serialNumber, action);
    }

    /// <summary>
    /// Sends cancelOrder action with 10s confirmation handshake (§13, §4).
    /// </summary>
    public async Task<bool> SendCancelOrderAsync(string manufacturer, string serialNumber)
    {
        var actionId = Guid.NewGuid().ToString("N");
        var action = new VdaAction
        {
            ActionId = actionId,
            ActionType = "cancelOrder",
            BlockingType = BlockingType.HARD,
            ActionDescription = "Cancel current order"
        };

        TrackedOrder? tracked = null;
        lock (_orderLock)
        {
            if (_activeOrdersBySerial.TryGetValue(serialNumber, out tracked))
            {
                tracked.PendingCancelActionId = actionId;

                tracked.CancelTimer?.Stop();
                tracked.CancelTimer?.Dispose();

                var timer = new System.Timers.Timer(10000);
                timer.AutoReset = false;
                timer.Elapsed += (s, e) =>
                {
                    NotificationRaised?.Invoke("WARNING", $"Cancel confirmation timeout (10s) for order '{tracked?.OrderId}' on AGV '{serialNumber}' (§13: cancel not acknowledged).");
                    _repository.InsertOrderEvent(tracked?.OrderId ?? "", serialNumber, "CANCEL_TIMEOUT", "{\"error\":\"Cancel not acknowledged within 10s\"}");
                };
                tracked.CancelTimer = timer;
                timer.Start();
            }
        }

        bool sent = await SendInstantActionAsync(manufacturer, serialNumber, action);
        if (tracked != null)
        {
            _repository.InsertOrderEvent(tracked.OrderId, serialNumber, "CANCEL_REQUESTED", $"{{\"actionId\":\"{actionId}\"}}");
        }
        return sent;
    }

    private async Task<bool> SendInstantActionAsync(string manufacturer, string serialNumber, VdaAction action)
    {
        var msg = new InstantActionsMessage
        {
            Manufacturer = manufacturer,
            SerialNumber = serialNumber,
            Actions = new List<VdaAction> { action }
        };

        await _mqttClient.PublishAsync(manufacturer, serialNumber, Vda5050Topic.InstantActions, msg, qos: 0, retain: false);
        return true;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_orderLock)
        {
            foreach (var order in _ordersById.Values)
            {
                order.AckTimer?.Stop();
                order.AckTimer?.Dispose();
                order.CancelTimer?.Stop();
                order.CancelTimer?.Dispose();
            }
            _activeOrdersBySerial.Clear();
            _ordersById.Clear();
        }

        _mqttClient.StateReceived -= OnStateReceived;
    }
}
