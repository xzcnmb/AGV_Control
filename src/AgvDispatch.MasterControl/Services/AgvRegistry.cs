using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.Vda5050.Json;
using AgvDispatch.Vda5050.Messages;

namespace AgvDispatch.MasterControl.Services;

/// <summary>
/// Registry tracking known AGVs by serial number.
/// Implements §4 (5s timer checking for >60s state inactivity) and §11 connection states.
/// Exposes an ObservableCollection for UI binding, updated strictly on the UI thread (§19).
/// </summary>
public class AgvRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, AgvEntry> _agvs = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Timers.Timer _watchdogTimer;
    private readonly TaskRepository? _repository;
    private bool _disposed;

    public ObservableCollection<AgvEntry> AgvList { get; } = new();

    public event Action<AgvEntry>? AgvUpdated;
    public event Action<AgvEntry>? AgvAdded;

    public AgvRegistry(TaskRepository? repository = null)
    {
        _repository = repository;

        // Watchdog timer running every 5 seconds (§4)
        _watchdogTimer = new System.Timers.Timer(5000);
        _watchdogTimer.Elapsed += (s, e) =>
        {
            try
            {
                CheckWatchdog();
            }
            catch
            {
                // §19: All timer callbacks wrapped in try/catch
            }
        };
        _watchdogTimer.AutoReset = true;
        _watchdogTimer.Start();
    }

    public AgvEntry GetOrAdd(string manufacturer, string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Serial number cannot be null or empty", nameof(serialNumber));
        }

        if (_agvs.TryGetValue(serialNumber, out var existing))
        {
            if (string.IsNullOrEmpty(existing.Manufacturer) && !string.IsNullOrEmpty(manufacturer))
            {
                existing.Manufacturer = manufacturer;
            }
            return existing;
        }

        var entry = new AgvEntry
        {
            Manufacturer = manufacturer,
            SerialNumber = serialNumber,
            ConnectionState = MasterConnectionState.Unknown
        };

        if (_agvs.TryAdd(serialNumber, entry))
        {
            RunOnUi(() =>
            {
                if (!AgvList.Any(a => string.Equals(a.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)))
                {
                    AgvList.Add(entry);
                }
            });

            AgvAdded?.Invoke(entry);
            return entry;
        }

        return _agvs[serialNumber];
    }

    public AgvEntry? GetAgv(string serialNumber)
    {
        _agvs.TryGetValue(serialNumber, out var entry);
        return entry;
    }

    public void OnConnectionReceived(ConnectionMessage msg)
    {
        var entry = GetOrAdd(msg.Manufacturer, msg.SerialNumber);
        var oldState = entry.ConnectionState;

        RunOnUi(() =>
        {
            entry.UpdateConnection(msg.ConnectionState);
        });

        if (oldState != entry.ConnectionState)
        {
            _repository?.InsertAgvEvent(msg.SerialNumber, "ConnectionStateChanged", Vda5050Json.Serialize(msg));
        }

        AgvUpdated?.Invoke(entry);
    }

    public void OnStateReceived(StateMessage msg)
    {
        var entry = GetOrAdd(msg.Manufacturer, msg.SerialNumber);

        RunOnUi(() =>
        {
            entry.UpdateState(msg);
        });

        // Record errors to repository if present (§18)
        if (msg.Errors != null && msg.Errors.Count > 0)
        {
            foreach (var err in msg.Errors)
            {
                var refJson = err.ErrorReferences != null ? Vda5050Json.Serialize(err.ErrorReferences) : "[]";
                _repository?.InsertError(
                    msg.SerialNumber,
                    err.ErrorType,
                    err.ErrorLevel.ToString(),
                    err.ErrorDescription ?? string.Empty,
                    refJson
                );
            }
        }

        AgvUpdated?.Invoke(entry);
    }

    public void OnFactsheetReceived(FactsheetMessage msg)
    {
        var entry = GetOrAdd(msg.Manufacturer, msg.SerialNumber);

        RunOnUi(() =>
        {
            entry.UpdateFactsheet(msg);
        });

        _repository?.InsertAgvEvent(msg.SerialNumber, "FactsheetReceived", Vda5050Json.Serialize(msg));
        AgvUpdated?.Invoke(entry);
    }

    public void OnVisualizationReceived(VisualizationMessage msg)
    {
        var entry = GetOrAdd(msg.Manufacturer, msg.SerialNumber);

        RunOnUi(() =>
        {
            entry.UpdateVisualization(msg);
        });

        AgvUpdated?.Invoke(entry);
    }

    private void CheckWatchdog()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _agvs.Values)
        {
            var oldState = entry.ConnectionState;
            var wasStale = entry.IsStale;

            RunOnUi(() =>
            {
                entry.CheckWatchdog(now, 60.0);
            });

            if (entry.ConnectionState != oldState || entry.IsStale != wasStale)
            {
                if (entry.IsStale && !wasStale)
                {
                    _repository?.InsertAgvEvent(entry.SerialNumber, "WatchdogStale", $"{{\"serialNumber\":\"{entry.SerialNumber}\",\"timeout\":60}}");
                }
                AgvUpdated?.Invoke(entry);
            }
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _watchdogTimer.Stop();
            _watchdogTimer.Dispose();
        }
        catch
        {
            // Ignore
        }
    }
}
