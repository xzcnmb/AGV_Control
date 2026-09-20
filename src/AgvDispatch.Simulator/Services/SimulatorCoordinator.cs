namespace AgvDispatch.Simulator.Services;

using System.Diagnostics;
using AgvDispatch.Vda5050;
using AgvDispatch.Vda5050.Enums;
using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;
using AgvDispatch.Vda5050.StateMachines;
using AgvDispatch.Vda5050.Topics;

/// <summary>
/// Central coordinator for the AGV Simulator per VDA5050 v2.0.0 contract:
/// - §4/§7: State reporting (30s periodic + event triggers: order received/updated, node traversed,
///          error/warning change, operatingMode change, driving change, node/edge/actionStates change, load change)
/// - §4/§7: High-freq visualization (200ms, 5Hz)
/// - §10/§13: InstantActions (startPause, stopPause, cancelOrder, factsheetRequest, stateRequest, charge)
/// - §11: Connection state machine integration
/// - §12: Order acceptance FSM with SequenceValidator & OrderAcceptance.Evaluate
/// - §13: CancelOrder handshake
/// - §14: Battery & charging action execution
/// - §15: Error & FATAL gate
/// - §17: Header stamping
/// - §19: Threading & exception guarding
/// </summary>
public class SimulatorCoordinator : IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly SimMqttClient _mqttClient;
    private readonly MovementEngine _movementEngine;
    private readonly BatterySim _batterySim;

    private readonly System.Timers.Timer _periodicStateTimer;
    private readonly System.Timers.Timer _visualizationTimer;
    private readonly System.Timers.Timer _batteryTimer;
    private readonly Stopwatch _batteryStopwatch = new();

    // Active order tracking
    private OrderMessage? _currentOrder;
    private string _orderId = string.Empty;
    private uint _orderUpdateId;
    private string _lastNodeId = string.Empty;
    private uint _lastNodeSequenceId;
    private string? _zoneSetId;

    // States per §7
    private readonly List<NodeState> _nodeStates = new();
    private readonly List<EdgeState> _edgeStates = new();
    private readonly List<ActionState> _actionStates = new();
    private readonly List<Error> _errors = new();
    private readonly List<Info> _information = new();
    private readonly List<Load> _loads = new();

    private OperatingMode _operatingMode = OperatingMode.AUTOMATIC;
    private SafetyState _safetyState = new() { EStop = EStop.NONE, FieldViolation = false };
    private bool _paused;
    private bool _newBaseRequest;
    private bool _driving;
    private bool _factsheetRequested;

    // Snapshot of previous action states for pause/resume
    private readonly Dictionary<string, ActionStatus> _prePausedActionStatuses = new();

    // Events for UI
    public event Action? StateUpdated;
    public event Action<string>? LogMessage;

    // Public properties for UI binding
    public SimMqttClient MqttClient => _mqttClient;
    public MovementEngine MovementEngine => _movementEngine;
    public BatterySim BatterySim => _batterySim;

    public OrderMessage? CurrentOrder { get { lock (_stateLock) return _currentOrder; } }
    public string OrderId { get { lock (_stateLock) return _orderId; } }
    public uint OrderUpdateId { get { lock (_stateLock) return _orderUpdateId; } }
    public string LastNodeId { get { lock (_stateLock) return _lastNodeId; } }
    public uint LastNodeSequenceId { get { lock (_stateLock) return _lastNodeSequenceId; } }
    public bool Paused { get { lock (_stateLock) return _paused; } }
    public bool NewBaseRequest { get { lock (_stateLock) return _newBaseRequest; } }
    public bool Driving { get { lock (_stateLock) return _driving; } }
    public OperatingMode OperatingMode { get { lock (_stateLock) return _operatingMode; } }
    public IReadOnlyList<NodeState> NodeStates { get { lock (_stateLock) return _nodeStates.ToList(); } }
    public IReadOnlyList<EdgeState> EdgeStates { get { lock (_stateLock) return _edgeStates.ToList(); } }
    public IReadOnlyList<ActionState> ActionStates { get { lock (_stateLock) return _actionStates.ToList(); } }
    public IReadOnlyList<Error> Errors { get { lock (_stateLock) return _errors.ToList(); } }

    public SimulatorCoordinator(SimMqttClient mqttClient, MovementEngine movementEngine, BatterySim batterySim)
    {
        _mqttClient = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient));
        _movementEngine = movementEngine ?? throw new ArgumentNullException(nameof(movementEngine));
        _batterySim = batterySim ?? throw new ArgumentNullException(nameof(batterySim));

        // Wire MQTT client events
        _mqttClient.OrderReceived += HandleOrderReceivedAsync;
        _mqttClient.InstantActionsReceived += HandleInstantActionsReceivedAsync;
        _mqttClient.Reconnected += HandleReconnectedAsync;
        _mqttClient.LogMessage += (msg) => LogMessage?.Invoke(msg);

        // Wire MovementEngine events
        _movementEngine.PositionChanged += OnPositionChanged;
        _movementEngine.NodeReached += OnNodeReached;
        _movementEngine.ReachedLastReleasedNode += OnReachedLastReleasedNode;
        _movementEngine.OrderCompleted += OnOrderCompleted;
        _movementEngine.LogMessage += (msg) => LogMessage?.Invoke(msg);

        // Timers per §4
        _periodicStateTimer = new System.Timers.Timer(30000); // 30s state periodic
        _periodicStateTimer.AutoReset = true;
        _periodicStateTimer.Elapsed += (s, e) => _ = PublishStateAsync();

        _visualizationTimer = new System.Timers.Timer(200); // 200ms (5Hz) visualization
        _visualizationTimer.AutoReset = true;
        _visualizationTimer.Elapsed += (s, e) => _ = PublishVisualizationAsync();

        _batteryTimer = new System.Timers.Timer(500); // 500ms battery tick
        _batteryTimer.AutoReset = true;
        _batteryTimer.Elapsed += OnBatteryTick;
    }

    public void Start()
    {
        _movementEngine.Start();
        _batteryStopwatch.Restart();
        _batteryTimer.Start();
        _periodicStateTimer.Start();
        _visualizationTimer.Start();
    }

    public void Stop()
    {
        _movementEngine.Stop();
        _batteryTimer.Stop();
        _periodicStateTimer.Stop();
        _visualizationTimer.Stop();
        _batteryStopwatch.Stop();
    }

    #region Order Acceptance FSM (§12)

    private async Task HandleOrderReceivedAsync(OrderMessage incoming)
    {
        Log($"Received Order: orderId='{incoming.OrderId}', orderUpdateId={incoming.OrderUpdateId}, nodes={incoming.Nodes.Count}, edges={incoming.Edges.Count}");

        bool triggerState = false;

        lock (_stateLock)
        {
            // §15: Fatal gate — If current errorLevel is FATAL, reject new orders
            if (_errors.Any(e => e.ErrorLevel == ErrorLevel.FATAL))
            {
                Log("Order rejected: AGV has active FATAL error (§15).");
                var fatalError = new Error
                {
                    ErrorType = "orderAcceptanceError",
                    ErrorLevel = ErrorLevel.WARNING,
                    ErrorDescription = "Order rejected due to active FATAL error",
                    ErrorReferences = new List<ErrorReference>
                    {
                        new("orderId", incoming.OrderId),
                        new("orderUpdateId", incoming.OrderUpdateId.ToString())
                    }
                };
                _errors.Add(fatalError);
                triggerState = true;
                goto EndOfOrder;
            }

            // §12 & §9: Validate sequence rules & released-prefix
            var validation = SequenceValidator.Validate(incoming);
            if (!validation.IsValid)
            {
                Log($"Order validation failed: {string.Join("; ", validation.Errors)}");
                var orderErr = new Error
                {
                    ErrorType = "orderValidationFailed",
                    ErrorLevel = ErrorLevel.WARNING,
                    ErrorDescription = string.Join("; ", validation.Errors),
                    ErrorReferences = new List<ErrorReference>
                    {
                        new("orderId", incoming.OrderId),
                        new("orderUpdateId", incoming.OrderUpdateId.ToString())
                    }
                };
                _errors.Add(orderErr);
                triggerState = true;
                goto EndOfOrder;
            }

            // Evaluate using OrderAcceptance state machine (§12)
            var decision = OrderAcceptance.Evaluate(_currentOrder, incoming, _lastNodeId, _lastNodeSequenceId);
            Log($"Order acceptance decision: {decision}");

            switch (decision)
            {
                case AcceptanceResult.Ignore:
                    // Case 2: Deduplication, ignore
                    Log($"Order {incoming.OrderId} update {incoming.OrderUpdateId} ignored (duplicate).");
                    break;

                case AcceptanceResult.RejectLowerUpdate:
                    // Case 3: Reject lower update
                    Log($"Order {incoming.OrderId} update {incoming.OrderUpdateId} rejected (lower than current {_orderUpdateId}).");
                    _errors.Add(new Error
                    {
                        ErrorType = "orderUpdateError",
                        ErrorLevel = ErrorLevel.WARNING,
                        ErrorDescription = $"Received lower orderUpdateId ({incoming.OrderUpdateId} < {_orderUpdateId})",
                        ErrorReferences = new List<ErrorReference>
                        {
                            new("orderId", incoming.OrderId),
                            new("orderUpdateId", incoming.OrderUpdateId.ToString())
                        }
                    });
                    triggerState = true;
                    break;

                case AcceptanceResult.StitchMismatch:
                    // Case 5 mismatch: Reject
                    Log($"Order {incoming.OrderId} update {incoming.OrderUpdateId} stitch mismatch.");
                    _errors.Add(new Error
                    {
                        ErrorType = "orderUpdateError",
                        ErrorLevel = ErrorLevel.WARNING,
                        ErrorDescription = "Order update stitching node does not match current order",
                        ErrorReferences = new List<ErrorReference>
                        {
                            new("orderId", incoming.OrderId),
                            new("orderUpdateId", incoming.OrderUpdateId.ToString())
                        }
                    });
                    triggerState = true;
                    break;

                case AcceptanceResult.Accept:
                    // Case 4: Accept brand new order (or replace current order)
                    Log($"Order {incoming.OrderId} accepted as new active order.");
                    _currentOrder = incoming;
                    _orderId = incoming.OrderId;
                    _orderUpdateId = incoming.OrderUpdateId;
                    _zoneSetId = incoming.ZoneSetId;
                    _newBaseRequest = false;

                    // Remove finished cancelOrder actionState if present (§13: keep it until a new order arrives)
                    _actionStates.RemoveAll(a => a.ActionType == "cancelOrder" && a.ActionStatus == ActionStatus.FINISHED);

                    // Seed nodeStates and edgeStates
                    _nodeStates.Clear();
                    foreach (var n in incoming.Nodes)
                    {
                        _nodeStates.Add(new NodeState
                        {
                            NodeId = n.NodeId,
                            SequenceId = n.SequenceId,
                            NodeDescription = n.NodeDescription,
                            Released = n.Released,
                            NodePosition = n.NodePosition
                        });
                    }

                    _edgeStates.Clear();
                    foreach (var e in incoming.Edges)
                    {
                        _edgeStates.Add(new EdgeState
                        {
                            EdgeId = e.EdgeId,
                            SequenceId = e.SequenceId,
                            EdgeDescription = e.EdgeDescription,
                            Released = e.Released,
                            Trajectory = e.Trajectory
                        });
                    }

                    // Seed actionStates from order nodes & edges
                    _actionStates.Clear();
                    foreach (var n in incoming.Nodes)
                    {
                        foreach (var a in n.Actions)
                        {
                            _actionStates.Add(new ActionState
                            {
                                ActionId = a.ActionId,
                                ActionType = a.ActionType,
                                ActionDescription = a.ActionDescription,
                                ActionStatus = ActionStatus.WAITING
                            });
                        }
                    }
                    foreach (var e in incoming.Edges)
                    {
                        foreach (var a in e.Actions)
                        {
                            _actionStates.Add(new ActionState
                            {
                                ActionId = a.ActionId,
                                ActionType = a.ActionType,
                                ActionDescription = a.ActionDescription,
                                ActionStatus = ActionStatus.WAITING
                            });
                        }
                    }

                    // Initialize movement engine route
                    _movementEngine.SetRoute(incoming.Nodes, incoming.Edges, 0);

                    // Update lastNodeId & lastNodeSequenceId if at first node
                    if (incoming.Nodes.Count > 0)
                    {
                        var firstNode = incoming.Nodes[0];
                        _lastNodeId = firstNode.NodeId;
                        _lastNodeSequenceId = firstNode.SequenceId;
                    }

                    triggerState = true;
                    break;

                case AcceptanceResult.StitchOk:
                    // Case 5: Stitch update to current order
                    Log($"Order {incoming.OrderId} update {incoming.OrderUpdateId} stitched successfully.");
                    _orderUpdateId = incoming.OrderUpdateId;
                    _newBaseRequest = false;

                    if (_currentOrder == null)
                    {
                        break;
                    }

                    // Merge from stitch node onward
                    var stitchNode = incoming.Nodes.FirstOrDefault(n => n.Released) ?? incoming.Nodes[0];
                    int existingStitchIdx = _currentOrder.Nodes.FindIndex(n => n.NodeId == stitchNode.NodeId && n.SequenceId == stitchNode.SequenceId);

                    if (existingStitchIdx >= 0)
                    {
                        // Keep nodes up to stitch node, append incoming nodes after stitch node
                        var keptNodes = _currentOrder.Nodes.Take(existingStitchIdx + 1).ToList();
                        var newNodes = incoming.Nodes.Skip(1).ToList();
                        var mergedNodes = keptNodes.Concat(newNodes).ToList();

                        // Keep edges up to stitch node index
                        var keptEdges = _currentOrder.Edges.Take(existingStitchIdx).ToList();
                        var newEdges = incoming.Edges;
                        var mergedEdges = keptEdges.Concat(newEdges).ToList();

                        _currentOrder.Nodes = mergedNodes;
                        _currentOrder.Edges = mergedEdges;
                        _currentOrder.OrderUpdateId = incoming.OrderUpdateId;

                        // Rebuild nodeStates / edgeStates for remaining parts
                        // Only replace nodeStates/edgeStates from stitch node onwards
                        int stateStitchNodeIdx = _nodeStates.FindIndex(ns => ns.NodeId == stitchNode.NodeId && ns.SequenceId == stitchNode.SequenceId);
                        if (stateStitchNodeIdx >= 0)
                        {
                            _nodeStates.RemoveRange(stateStitchNodeIdx + 1, _nodeStates.Count - (stateStitchNodeIdx + 1));
                            foreach (var n in incoming.Nodes.Skip(1))
                            {
                                _nodeStates.Add(new NodeState
                                {
                                    NodeId = n.NodeId,
                                    SequenceId = n.SequenceId,
                                    NodeDescription = n.NodeDescription,
                                    Released = n.Released,
                                    NodePosition = n.NodePosition
                                });
                            }
                        }

                        // Also update edgeStates
                        int stateStitchEdgeIdx = _edgeStates.FindIndex(es => es.SequenceId >= stitchNode.SequenceId);
                        if (stateStitchEdgeIdx >= 0)
                        {
                            _edgeStates.RemoveRange(stateStitchEdgeIdx, _edgeStates.Count - stateStitchEdgeIdx);
                        }
                        foreach (var e in incoming.Edges)
                        {
                            _edgeStates.Add(new EdgeState
                            {
                                EdgeId = e.EdgeId,
                                SequenceId = e.SequenceId,
                                EdgeDescription = e.EdgeDescription,
                                Released = e.Released,
                                Trajectory = e.Trajectory
                            });
                        }

                        // Add new actionStates
                        foreach (var n in incoming.Nodes.Skip(1))
                        {
                            foreach (var a in n.Actions)
                            {
                                if (!_actionStates.Any(as_ => as_.ActionId == a.ActionId))
                                {
                                    _actionStates.Add(new ActionState
                                    {
                                        ActionId = a.ActionId,
                                        ActionType = a.ActionType,
                                        ActionDescription = a.ActionDescription,
                                        ActionStatus = ActionStatus.WAITING
                                    });
                                }
                            }
                        }
                        foreach (var e in incoming.Edges)
                        {
                            foreach (var a in e.Actions)
                            {
                                if (!_actionStates.Any(as_ => as_.ActionId == a.ActionId))
                                {
                                    _actionStates.Add(new ActionState
                                    {
                                        ActionId = a.ActionId,
                                        ActionType = a.ActionType,
                                        ActionDescription = a.ActionDescription,
                                        ActionStatus = ActionStatus.WAITING
                                    });
                                }
                            }
                        }

                        // Update movement engine route
                        _movementEngine.SetRoute(mergedNodes, mergedEdges, existingStitchIdx);
                    }

                    triggerState = true;
                    break;
            }
        }

    EndOfOrder:
        StateUpdated?.Invoke();
        if (triggerState)
        {
            await PublishStateAsync();
        }
    }

    #endregion

    #region Instant Actions Handling (§10, §13, §14)

    private async Task HandleInstantActionsReceivedAsync(InstantActionsMessage msg)
    {
        Log($"Received InstantActions with {msg.Actions.Count} actions.");

        foreach (var action in msg.Actions)
        {
            await ExecuteInstantActionAsync(action);
        }
    }

    private async Task ExecuteInstantActionAsync(VdaAction action)
    {
        Log($"Processing instant action: id='{action.ActionId}', type='{action.ActionType}'");

        switch (action.ActionType)
        {
            case "startPause":
                await HandleStartPauseActionAsync(action);
                break;

            case "stopPause":
                await HandleStopPauseActionAsync(action);
                break;

            case "cancelOrder":
                await HandleCancelOrderActionAsync(action);
                break;

            case "factsheetRequest":
                await HandleFactsheetRequestActionAsync(action);
                break;

            case "stateRequest":
                await HandleStateRequestActionAsync(action);
                break;

            case "charge":
            case "startCharging":
                await HandleChargeActionAsync(action);
                break;

            default:
                await HandleUnknownInstantActionAsync(action);
                break;
        }
    }

    private async Task HandleStartPauseActionAsync(VdaAction action)
    {
        lock (_stateLock)
        {
            var actState = new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.RUNNING
            };
            _actionStates.Add(actState);

            _paused = true;
            _movementEngine.Paused = true;
            _driving = false;

            // Pause WAITING and RUNNING order actions
            foreach (var a in _actionStates.Where(a => a.ActionId != action.ActionId))
            {
                if (a.ActionStatus == ActionStatus.RUNNING || a.ActionStatus == ActionStatus.WAITING)
                {
                    _prePausedActionStatuses[a.ActionId] = a.ActionStatus;
                    a.ActionStatus = ActionStatus.PAUSED;
                }
            }

            actState.ActionStatus = ActionStatus.FINISHED;
        }

        StateUpdated?.Invoke();
        await PublishStateAsync();
    }

    private async Task HandleStopPauseActionAsync(VdaAction action)
    {
        lock (_stateLock)
        {
            var actState = new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.RUNNING
            };
            _actionStates.Add(actState);

            _paused = false;
            _movementEngine.Paused = false;

            // Restore PAUSED actions to their previous status
            foreach (var a in _actionStates.Where(a => a.ActionId != action.ActionId))
            {
                if (a.ActionStatus == ActionStatus.PAUSED && _prePausedActionStatuses.TryGetValue(a.ActionId, out var prevStatus))
                {
                    a.ActionStatus = prevStatus;
                }
            }
            _prePausedActionStatuses.Clear();

            actState.ActionStatus = ActionStatus.FINISHED;
        }

        StateUpdated?.Invoke();
        await PublishStateAsync();
    }

    /// <summary>
    /// §13 Cancel handshake (HARD):
    /// 1. Stop movement engine immediately (driving=false)
    /// 2. Add actionState {actionId, cancelOrder, RUNNING}, publish state immediately
    /// 3. Delete order: nodeStates=[], edgeStates=[], cancel order actions, paused=false, newBaseRequest=false
    /// 4. Set cancelOrder actionState FINISHED, publish state; KEEP it until a new order arrives
    /// 5. If no active order: add error NO_ORDER_TO_CANCEL (WARNING), action FAILED
    /// </summary>
    private async Task HandleCancelOrderActionAsync(VdaAction action)
    {
        bool hasActiveOrder;
        ActionState cancelActState;

        lock (_stateLock)
        {
            hasActiveOrder = !string.IsNullOrEmpty(_orderId) || _currentOrder != null;

            if (!hasActiveOrder)
            {
                // §13: No active order -> error NO_ORDER_TO_CANCEL (WARNING), action FAILED
                Log("CancelOrder received but no active order exists (§13).");
                cancelActState = new ActionState
                {
                    ActionId = action.ActionId,
                    ActionType = action.ActionType,
                    ActionDescription = action.ActionDescription,
                    ActionStatus = ActionStatus.FAILED,
                    ResultDescription = "No active order to cancel"
                };
                _actionStates.Add(cancelActState);

                _errors.Add(new Error
                {
                    ErrorType = "NO_ORDER_TO_CANCEL",
                    ErrorLevel = ErrorLevel.WARNING,
                    ErrorDescription = "Received cancelOrder instant action but no active order is running",
                    ErrorReferences = new List<ErrorReference>
                    {
                        new("actionId", action.ActionId)
                    }
                });

                goto PublishAndExit;
            }

            // Step 1: Stop immediately
            Log("CancelOrder received: stopping AGV immediately (§13).");
            _movementEngine.Stop();
            _movementEngine.ClearRoute();
            _driving = false;

            // Step 2: Add cancelOrder RUNNING
            cancelActState = new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.RUNNING
            };
            _actionStates.Add(cancelActState);
        }

        StateUpdated?.Invoke();
        // Publish state with cancelOrder = RUNNING
        await PublishStateAsync();

        lock (_stateLock)
        {
            // Step 3: Delete order.
            // VDA5050 §6.9: after cancelOrder the AGV clears node/edge state and stops,
            // but KEEPS reporting the last orderId/orderUpdateId in its state. Clearing
            // orderId would make the master unable to match the cancel-confirmation state
            // to the tracked order (its OnStateReceived filters by orderId), so the
            // cancel handshake would never complete and always time out. Preserve them.
            _currentOrder = null;
            _nodeStates.Clear();
            _edgeStates.Clear();
            _paused = false;
            _movementEngine.Paused = false;
            _newBaseRequest = false;

            // Cancel any pending order actions
            foreach (var a in _actionStates.Where(a => a.ActionId != action.ActionId))
            {
                if (a.ActionStatus == ActionStatus.WAITING || a.ActionStatus == ActionStatus.RUNNING || a.ActionStatus == ActionStatus.PAUSED)
                {
                    a.ActionStatus = ActionStatus.FAILED;
                    a.ResultDescription = "Order cancelled";
                }
            }

            // Step 4: cancelOrder FINISHED, keep until new order arrives
            cancelActState.ActionStatus = ActionStatus.FINISHED;
            cancelActState.ResultDescription = "Order successfully cancelled";
        }

    PublishAndExit:
        StateUpdated?.Invoke();
        await PublishStateAsync();
    }

    private async Task HandleFactsheetRequestActionAsync(VdaAction action)
    {
        lock (_stateLock)
        {
            _actionStates.Add(new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.FINISHED
            });
            _factsheetRequested = true;
        }

        await PublishFactsheetAsync();
        StateUpdated?.Invoke();
        await PublishStateAsync();
    }

    private async Task HandleStateRequestActionAsync(VdaAction action)
    {
        lock (_stateLock)
        {
            _actionStates.Add(new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.FINISHED
            });
        }

        StateUpdated?.Invoke();
        await PublishStateAsync();
    }

    private async Task HandleChargeActionAsync(VdaAction action)
    {
        ActionState chargeActState;
        lock (_stateLock)
        {
            chargeActState = new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.RUNNING
            };
            _actionStates.Add(chargeActState);

            // §14: charge action HARD-blocks movement
            _movementEngine.IsCharging = true;
            _batterySim.Charging = true;
            _driving = false;
        }

        StateUpdated?.Invoke();
        await PublishStateAsync();

        // Simulate charging duration asynchronously (e.g. 5 seconds for simulation)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(5000);
                lock (_stateLock)
                {
                    _batterySim.Charging = false;
                    _movementEngine.IsCharging = false;
                    chargeActState.ActionStatus = ActionStatus.FINISHED;
                }
                StateUpdated?.Invoke();
                await PublishStateAsync();
            }
            catch (Exception ex)
            {
                Log($"Error completing charge action: {ex.Message}");
            }
        });
    }

    private async Task HandleUnknownInstantActionAsync(VdaAction action)
    {
        lock (_stateLock)
        {
            _actionStates.Add(new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.FAILED,
                ResultDescription = $"Unsupported actionType '{action.ActionType}'"
            });
        }

        StateUpdated?.Invoke();
        await PublishStateAsync();
    }

    #endregion

    #region Node / Edge Traversal & Action Execution

    private void OnPositionChanged(AgvPosition pos, Velocity vel)
    {
        bool drivingChanged = false;
        lock (_stateLock)
        {
            bool nowDriving = _movementEngine.IsDriving;
            if (_driving != nowDriving)
            {
                _driving = nowDriving;
                drivingChanged = true;
            }
        }

        if (drivingChanged)
        {
            StateUpdated?.Invoke();
            _ = PublishStateAsync();
        }
    }

    private void OnNodeReached(Node node, Edge? edge)
    {
        Log($"Node reached: nodeId='{node.NodeId}', seq={node.SequenceId}");

        lock (_stateLock)
        {
            _lastNodeId = node.NodeId;
            _lastNodeSequenceId = node.SequenceId;

            // Remove traversed node and incoming edge from nodeStates and edgeStates
            _nodeStates.RemoveAll(n => n.NodeId == node.NodeId && n.SequenceId <= node.SequenceId);
            if (edge != null)
            {
                _edgeStates.RemoveAll(e => e.EdgeId == edge.EdgeId && e.SequenceId <= edge.SequenceId);
            }

            // Execute node actions
            foreach (var act in node.Actions)
            {
                ExecuteOrderBoundAction(act);
            }
        }

        StateUpdated?.Invoke();
        // §7: node traversed is an immediate state trigger
        _ = PublishStateAsync();
    }

    private void ExecuteOrderBoundAction(VdaAction action)
    {
        var actState = _actionStates.FirstOrDefault(a => a.ActionId == action.ActionId);
        if (actState == null)
        {
            actState = new ActionState
            {
                ActionId = action.ActionId,
                ActionType = action.ActionType,
                ActionDescription = action.ActionDescription,
                ActionStatus = ActionStatus.WAITING
            };
            _actionStates.Add(actState);
        }

        actState.ActionStatus = ActionStatus.RUNNING;

        if (action.ActionType == "charge" || action.ActionType == "startCharging")
        {
            // §14: Charge action on node
            _movementEngine.IsCharging = true;
            _batterySim.Charging = true;

            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                lock (_stateLock)
                {
                    _batterySim.Charging = false;
                    _movementEngine.IsCharging = false;
                    actState.ActionStatus = ActionStatus.FINISHED;
                }
                StateUpdated?.Invoke();
                await PublishStateAsync();
            });
        }
        else
        {
            // Complete generic action quickly
            actState.ActionStatus = ActionStatus.FINISHED;
        }
    }

    private void OnReachedLastReleasedNode()
    {
        Log("Reached last released node. Horizon remains -> newBaseRequest=true");
        lock (_stateLock)
        {
            _newBaseRequest = true;
        }
        StateUpdated?.Invoke();
        _ = PublishStateAsync();
    }

    private void OnOrderCompleted()
    {
        Log("Order traversal completed.");
        lock (_stateLock)
        {
            _driving = false;
            _nodeStates.Clear();
            _edgeStates.Clear();
            _newBaseRequest = false;
        }
        StateUpdated?.Invoke();
        _ = PublishStateAsync();
    }

    #endregion

    #region Battery & Error Management

    private void OnBatteryTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            double elapsed = _batteryStopwatch.Elapsed.TotalSeconds;
            _batteryStopwatch.Restart();

            if (elapsed > 2.0) elapsed = 0.5;

            bool isDriving;
            lock (_stateLock)
            {
                isDriving = _driving;
            }

            _batterySim.Tick(elapsed, isDriving);
        }
        catch (Exception ex)
        {
            Log($"Exception in OnBatteryTick: {ex.Message}");
        }
    }

    public void InjectWarning(string errorType, string description)
    {
        lock (_stateLock)
        {
            _errors.Add(new Error
            {
                ErrorType = errorType,
                ErrorLevel = ErrorLevel.WARNING,
                ErrorDescription = description,
                ErrorReferences = new List<ErrorReference>
                {
                    new("source", "user_injection")
                }
            });
        }

        StateUpdated?.Invoke();
        _ = PublishStateAsync();
    }

    public void InjectFatal(string errorType, string description)
    {
        lock (_stateLock)
        {
            _errors.Add(new Error
            {
                ErrorType = errorType,
                ErrorLevel = ErrorLevel.FATAL,
                ErrorDescription = description,
                ErrorReferences = new List<ErrorReference>
                {
                    new("source", "user_injection")
                }
            });

            // §15: FATAL stops movement immediately
            _movementEngine.HasFatalError = true;
            _driving = false;
        }

        StateUpdated?.Invoke();
        _ = PublishStateAsync();
    }

    public void ClearErrors()
    {
        lock (_stateLock)
        {
            _errors.Clear();
            _movementEngine.HasFatalError = false;
        }

        StateUpdated?.Invoke();
        _ = PublishStateAsync();
    }

    public void SetOperatingMode(OperatingMode mode)
    {
        lock (_stateLock)
        {
            if (_operatingMode != mode)
            {
                _operatingMode = mode;
                StateUpdated?.Invoke();
                _ = PublishStateAsync();
            }
        }
    }

    #endregion

    #region Publishing (State, Visualization, Factsheet)

    public async Task PublishStateAsync()
    {
        if (!_mqttClient.IsConnected)
        {
            return;
        }

        StateMessage state;
        lock (_stateLock)
        {
            state = new StateMessage
            {
                OrderId = _orderId,
                OrderUpdateId = _orderUpdateId,
                LastNodeId = _lastNodeId,
                LastNodeSequenceId = _lastNodeSequenceId,
                Driving = _driving,
                NodeStates = _nodeStates.Select(n => new NodeState
                {
                    NodeId = n.NodeId,
                    SequenceId = n.SequenceId,
                    NodeDescription = n.NodeDescription,
                    Released = n.Released,
                    NodePosition = n.NodePosition
                }).ToList(),
                EdgeStates = _edgeStates.Select(e => new EdgeState
                {
                    EdgeId = e.EdgeId,
                    SequenceId = e.SequenceId,
                    EdgeDescription = e.EdgeDescription,
                    Released = e.Released,
                    Trajectory = e.Trajectory
                }).ToList(),
                ActionStates = _actionStates.Select(a => new ActionState
                {
                    ActionId = a.ActionId,
                    ActionType = a.ActionType,
                    ActionDescription = a.ActionDescription,
                    ActionStatus = a.ActionStatus,
                    ResultDescription = a.ResultDescription
                }).ToList(),
                BatteryState = _batterySim.GetSnapshot(),
                OperatingMode = _operatingMode,
                Errors = _errors.Select(err => new Error
                {
                    ErrorType = err.ErrorType,
                    ErrorLevel = err.ErrorLevel,
                    ErrorDescription = err.ErrorDescription,
                    ErrorHint = err.ErrorHint,
                    ErrorReferences = err.ErrorReferences?.Select(r => new ErrorReference(r.ReferenceKey, r.ReferenceValue)).ToList()
                }).ToList(),
                SafetyState = new SafetyState
                {
                    EStop = _safetyState.EStop,
                    FieldViolation = _safetyState.FieldViolation
                },
                Information = _information.ToList(),
                AgvPosition = _movementEngine.CurrentPosition,
                Velocity = _movementEngine.CurrentVelocity,
                Paused = _paused ? true : null,
                NewBaseRequest = _newBaseRequest ? true : null,
                ZoneSetId = _zoneSetId
            };
        }

        await _mqttClient.PublishAsync(Vda5050Topic.State, state);
    }

    public async Task PublishVisualizationAsync()
    {
        if (!_mqttClient.IsConnected)
        {
            return;
        }

        var vis = new VisualizationMessage
        {
            AgvPosition = _movementEngine.CurrentPosition,
            Velocity = _movementEngine.CurrentVelocity
        };

        await _mqttClient.PublishAsync(Vda5050Topic.Visualization, vis);
    }

    public async Task PublishFactsheetAsync()
    {
        if (!_mqttClient.IsConnected)
        {
            return;
        }

        var factsheet = new FactsheetMessage
        {
            TypeSpecification = new TypeSpecification
            {
                SeriesName = "AgvSimSeries",
                SeriesDescription = "VDA5050 Simulated AGV",
                AgvKinematics = "DIFF",
                AgvClass = "CARRIER"
            },
            PhysicalParameters = new PhysicalParameters
            {
                SpeedMin = 0.1,
                SpeedMax = 2.0,
                AccelerationMax = 1.0,
                DecelerationMax = 1.0,
                Width = 0.8,
                Length = 1.2,
                HeightMin = 0.3,
                HeightMax = 0.5
            },
            ProtocolLimits = new ProtocolLimits
            {
                Timing = new TimingLimits
                {
                    DefaultStateInterval = 30.0,
                    VisualizationInterval = 0.2
                }
            },
            ProtocolFeatures = new ProtocolFeatures
            {
                AgvActions = new List<AgvActionSpecification>
                {
                    new() { ActionType = "charge", ActionDescription = "Recharge AGV battery", ActionScopes = new() { "NODE", "INSTANT" } },
                    new() { ActionType = "startPause", ActionDescription = "Pause AGV motion", ActionScopes = new() { "INSTANT" } },
                    new() { ActionType = "stopPause", ActionDescription = "Resume AGV motion", ActionScopes = new() { "INSTANT" } },
                    new() { ActionType = "cancelOrder", ActionDescription = "Cancel current order", ActionScopes = new() { "INSTANT" } },
                    new() { ActionType = "stateRequest", ActionDescription = "Request immediate state report", ActionScopes = new() { "INSTANT" } },
                    new() { ActionType = "factsheetRequest", ActionDescription = "Request factsheet", ActionScopes = new() { "INSTANT" } }
                }
            },
            AgvGeometry = new AgvGeometry(),
            LoadSpecification = new LoadSpecification(),
            LocalizationParameters = new LocalizationParameters()
        };

        await _mqttClient.PublishAsync(Vda5050Topic.Factsheet, factsheet);
    }

    private async Task HandleReconnectedAsync()
    {
        // Per §11: On reconnect -> re-arm will + publish ONLINE + republish factsheet (if requested) + immediate state
        if (_factsheetRequested)
        {
            await PublishFactsheetAsync();
        }
        await PublishStateAsync();
    }

    #endregion

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [Coordinator] {message}");
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        _periodicStateTimer.Dispose();
        _visualizationTimer.Dispose();
        _batteryTimer.Dispose();
        await _mqttClient.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
