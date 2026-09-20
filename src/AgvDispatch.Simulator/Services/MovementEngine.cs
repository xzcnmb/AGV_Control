namespace AgvDispatch.Simulator.Services;

using System.Diagnostics;
using AgvDispatch.Vda5050.Models;

/// <summary>
/// Drives the AGV along released nodes and edges of the current order.
/// 100ms tick, interpolate x/y/theta by real elapsed time (Stopwatch per §4 and §19).
/// Raises events:
/// - PositionChanged (for visualization)
/// - NodeReached (node traversed → coordinator removes node/incoming edge, updates lastNodeId/lastNodeSequenceId, fires actions)
/// - ReachedLastReleasedNode (when horizon remains → set newBaseRequest)
/// - OrderCompleted
/// Honors:
/// - Paused (freeze movement)
/// - FatalError (stops movement per §15)
/// - Charging (blocks movement per §14)
/// </summary>
public class MovementEngine
{
    private readonly object _lock = new();
    private readonly System.Timers.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private List<Node> _nodes = new();
    private List<Edge> _edges = new();
    private int _currentNodeIndex; // index into _nodes
    private double _edgeProgress; // 0.0 to 1.0 along current edge between _nodes[i] and _nodes[i+1]
    private double _currentEdgeLength;

    private AgvPosition _currentPosition = new()
    {
        X = 0,
        Y = 0,
        Theta = 0,
        MapId = "default",
        PositionInitialized = true,
        LocalizationScore = 1.0
    };

    private Velocity _currentVelocity = new()
    {
        Vx = 0,
        Vy = 0,
        Omega = 0
    };

    public double SpeedMps { get; set; } = 1.0; // configurable speed, e.g. 1.0 m/s
    public bool Paused { get; set; }
    public bool HasFatalError { get; set; }
    public bool IsCharging { get; set; }
    public bool IsDriving { get; private set; }

    public AgvPosition CurrentPosition
    {
        get
        {
            lock (_lock)
            {
                return ClonePosition(_currentPosition);
            }
        }
    }

    public Velocity CurrentVelocity
    {
        get
        {
            lock (_lock)
            {
                return new Velocity
                {
                    Vx = _currentVelocity.Vx,
                    Vy = _currentVelocity.Vy,
                    Omega = _currentVelocity.Omega
                };
            }
        }
    }

    // Events
    public event Action<AgvPosition, Velocity>? PositionChanged;
    public event Action<Node, Edge?>? NodeReached;
    public event Action? ReachedLastReleasedNode;
    public event Action? OrderCompleted;
    public event Action<string>? LogMessage;

    public MovementEngine()
    {
        _timer = new System.Timers.Timer(100); // 100ms tick per §4
        _timer.AutoReset = true;
        _timer.Elapsed += OnTick;
    }

    public void Start()
    {
        _stopwatch.Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _stopwatch.Stop();
        lock (_lock)
        {
            SetDriving(false);
            _currentVelocity.Vx = 0;
            _currentVelocity.Vy = 0;
            _currentVelocity.Omega = 0;
        }
    }

    public void SetInitialPosition(double x, double y, double theta, string mapId = "default")
    {
        lock (_lock)
        {
            _currentPosition.X = x;
            _currentPosition.Y = y;
            _currentPosition.Theta = theta;
            _currentPosition.MapId = mapId;
            _currentPosition.PositionInitialized = true;
        }
        PositionChanged?.Invoke(CurrentPosition, CurrentVelocity);
    }

    /// <summary>
    /// Loads or updates the active route for the engine.
    /// </summary>
    public void SetRoute(List<Node> nodes, List<Edge> edges, int startNodeIndex = 0)
    {
        lock (_lock)
        {
            _nodes = new List<Node>(nodes);
            _edges = new List<Edge>(edges);
            _currentNodeIndex = startNodeIndex;
            _edgeProgress = 0.0;

            if (_currentNodeIndex < _nodes.Count)
            {
                var startNode = _nodes[_currentNodeIndex];
                if (startNode.NodePosition != null)
                {
                    _currentPosition.X = startNode.NodePosition.X;
                    _currentPosition.Y = startNode.NodePosition.Y;
                    if (startNode.NodePosition.Theta.HasValue)
                    {
                        _currentPosition.Theta = startNode.NodePosition.Theta.Value;
                    }
                    _currentPosition.MapId = startNode.NodePosition.MapId;
                }
            }

            UpdateEdgeLength();
        }
    }

    public void ClearRoute()
    {
        lock (_lock)
        {
            _nodes.Clear();
            _edges.Clear();
            _currentNodeIndex = 0;
            _edgeProgress = 0.0;
            SetDriving(false);
            _currentVelocity.Vx = 0;
            _currentVelocity.Vy = 0;
            _currentVelocity.Omega = 0;
        }
    }

    private void UpdateEdgeLength()
    {
        if (_currentNodeIndex < _edges.Count && _currentNodeIndex + 1 < _nodes.Count)
        {
            var p1 = _nodes[_currentNodeIndex].NodePosition;
            var p2 = _nodes[_currentNodeIndex + 1].NodePosition;
            if (p1 != null && p2 != null)
            {
                var dx = p2.X - p1.X;
                var dy = p2.Y - p1.Y;
                _currentEdgeLength = Math.Sqrt(dx * dx + dy * dy);
                return;
            }
        }
        _currentEdgeLength = 0;
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            if (elapsedSeconds > 1.0)
            {
                // Cap elapsed time if paused or debugger stalled
                elapsedSeconds = 0.1;
            }

            Tick(elapsedSeconds);
        }
        catch (Exception ex)
        {
            // Per §19: timer exceptions must be caught
            LogMessage?.Invoke($"[MovementEngine] Exception in OnTick: {ex.Message}");
        }
    }

    public void Tick(double elapsedSeconds)
    {
        Node? reachedNode = null;
        Edge? reachedEdge = null;
        bool isLastReleased = false;
        bool isComplete = false;
        AgvPosition posSnapshot;
        Velocity velSnapshot;

        lock (_lock)
        {
            // Check gating conditions: Paused, FatalError, Charging
            if (Paused || HasFatalError || IsCharging)
            {
                SetDriving(false);
                _currentVelocity.Vx = 0;
                _currentVelocity.Vy = 0;
                _currentVelocity.Omega = 0;
                posSnapshot = ClonePosition(_currentPosition);
                velSnapshot = new Velocity { Vx = 0, Vy = 0, Omega = 0 };
                return;
            }

            // If no more edges or nodes to traverse
            if (_nodes.Count == 0 || _currentNodeIndex >= _nodes.Count - 1)
            {
                SetDriving(false);
                _currentVelocity.Vx = 0;
                _currentVelocity.Vy = 0;
                _currentVelocity.Omega = 0;
                return;
            }

            // Check if next node / edge is released
            var currentEdge = _currentNodeIndex < _edges.Count ? _edges[_currentNodeIndex] : null;
            var nextNode = _currentNodeIndex + 1 < _nodes.Count ? _nodes[_currentNodeIndex + 1] : null;

            if (currentEdge == null || !currentEdge.Released || nextNode == null || !nextNode.Released)
            {
                // Cannot proceed because next segment is horizon (unreleased)
                SetDriving(false);
                _currentVelocity.Vx = 0;
                _currentVelocity.Vy = 0;
                _currentVelocity.Omega = 0;

                // We reached the boundary of the released base!
                // Check if there are still unreleased nodes in the order
                if (_nodes.Any(n => !n.Released))
                {
                    isLastReleased = true;
                }
                posSnapshot = ClonePosition(_currentPosition);
                velSnapshot = new Velocity { Vx = 0, Vy = 0, Omega = 0 };
            }
            else
            {
                // We can drive along currentEdge
                SetDriving(true);

                var startNode = _nodes[_currentNodeIndex];
                var p1 = startNode.NodePosition ?? new NodePosition { X = _currentPosition.X, Y = _currentPosition.Y };
                var p2 = nextNode.NodePosition ?? p1;

                var dx = p2.X - p1.X;
                var dy = p2.Y - p1.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                double speed = SpeedMps;
                if (currentEdge.MaxSpeed.HasValue && currentEdge.MaxSpeed.Value > 0)
                {
                    speed = Math.Min(speed, currentEdge.MaxSpeed.Value);
                }

                double advanceDist = speed * elapsedSeconds;
                double targetAngle = Math.Atan2(dy, dx);

                if (dist <= 0.0001 || _edgeProgress * dist + advanceDist >= dist)
                {
                    // Node reached!
                    _currentNodeIndex++;
                    _edgeProgress = 0.0;
                    _currentPosition.X = p2.X;
                    _currentPosition.Y = p2.Y;
                    _currentPosition.Theta = p2.Theta ?? targetAngle;
                    _currentPosition.MapId = p2.MapId;

                    _currentVelocity.Vx = 0;
                    _currentVelocity.Vy = 0;
                    _currentVelocity.Omega = 0;

                    reachedNode = nextNode;
                    reachedEdge = currentEdge;

                    UpdateEdgeLength();

                    // Check if that was the last node in the order
                    if (_currentNodeIndex >= _nodes.Count - 1)
                    {
                        SetDriving(false);
                        // Check if there are unreleased nodes in horizon
                        if (_nodes.Any(n => !n.Released))
                        {
                            isLastReleased = true;
                        }
                        else
                        {
                            isComplete = true;
                        }
                    }
                    else
                    {
                        // Check if next node is unreleased
                        var afterNextNode = _nodes[_currentNodeIndex + 1];
                        if (!afterNextNode.Released)
                        {
                            isLastReleased = true;
                        }
                    }
                }
                else
                {
                    // Interpolate
                    double currentTraveled = _edgeProgress * dist + advanceDist;
                    _edgeProgress = currentTraveled / dist;

                    _currentPosition.X = p1.X + dx * _edgeProgress;
                    _currentPosition.Y = p1.Y + dy * _edgeProgress;
                    _currentPosition.Theta = targetAngle;
                    _currentPosition.MapId = p1.MapId;

                    _currentVelocity.Vx = speed * Math.Cos(targetAngle);
                    _currentVelocity.Vy = speed * Math.Sin(targetAngle);
                    _currentVelocity.Omega = 0;
                }

                posSnapshot = ClonePosition(_currentPosition);
                velSnapshot = new Velocity
                {
                    Vx = _currentVelocity.Vx,
                    Vy = _currentVelocity.Vy,
                    Omega = _currentVelocity.Omega
                };
            }
        }

        // Fire events outside lock
        PositionChanged?.Invoke(posSnapshot, velSnapshot);

        if (reachedNode != null)
        {
            NodeReached?.Invoke(reachedNode, reachedEdge);
        }

        if (isLastReleased)
        {
            ReachedLastReleasedNode?.Invoke();
        }

        if (isComplete)
        {
            OrderCompleted?.Invoke();
        }
    }

    private void SetDriving(bool driving)
    {
        IsDriving = driving;
    }

    private static AgvPosition ClonePosition(AgvPosition pos)
    {
        return new AgvPosition
        {
            X = pos.X,
            Y = pos.Y,
            Theta = pos.Theta,
            MapId = pos.MapId,
            PositionInitialized = pos.PositionInitialized,
            LocalizationScore = pos.LocalizationScore,
            DeviationRange = pos.DeviationRange,
            MapDescription = pos.MapDescription
        };
    }
}
