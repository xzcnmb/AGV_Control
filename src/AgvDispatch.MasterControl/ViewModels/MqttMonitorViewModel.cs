using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgvDispatch.MasterControl.Models;
using AgvDispatch.MasterControl.Services;

namespace AgvDispatch.MasterControl.ViewModels;

/// <summary>
/// MQTT traffic monitor view model implementing dual-capture (§20) with a 500-entry bounded ring buffer.
/// Batches UI updates via a DispatcherTimer to prevent UI thread thrashing (§19).
/// </summary>
public partial class MqttMonitorViewModel : ObservableObject, IDisposable
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<MqttLogItem> _pendingItems = new();
    // Full bounded backing store of everything captured (unfiltered). LogEntries is the
    // filtered projection of this. Keeping the full history here lets a filter change
    // re-project already-captured items instead of only affecting future traffic.
    private readonly LinkedList<MqttLogItem> _allItems = new();
    private readonly DispatcherTimer _batchTimer;
    private readonly EmbeddedBroker _broker;
    private readonly MqttClientService _client;
    private bool _disposed;

    public ObservableCollection<MqttLogItem> LogEntries { get; } = new();

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _selectedDirection = "全部";

    public List<string> DirectionOptions { get; } = new() { "全部", "接收 (IN)", "发送 (OUT)", "自发回显 (sent(echo))" };

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

    private int _totalCaptured;
    public int TotalCaptured
    {
        get => _totalCaptured;
        private set => SetProperty(ref _totalCaptured, value);
    }

    public MqttMonitorViewModel(EmbeddedBroker broker, MqttClientService client)
    {
        _broker = broker;
        _client = client;

        // Broker-side capture
        _broker.MessageIntercepted += OnLogItemReceived;

        // Client-side capture (outgoing and echoes)
        _client.ClientMessageLogged += OnLogItemReceived;

        // Batch timer for UI updates (150ms)
        _batchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _batchTimer.Tick += OnBatchTimerTick;
        _batchTimer.Start();
    }

    private void OnLogItemReceived(MqttLogItem item)
    {
        _pendingItems.Enqueue(item);
        Interlocked.Increment(ref _totalCaptured);
    }

    private void OnBatchTimerTick(object? sender, EventArgs e)
    {
        if (_pendingItems.IsEmpty) return;

        var dequeued = new List<MqttLogItem>();
        while (_pendingItems.TryDequeue(out var item))
        {
            dequeued.Add(item);
        }

        if (dequeued.Count == 0) return;

        // Append to the full backing store, keeping it bounded to 500 (§20).
        foreach (var item in dequeued)
        {
            _allItems.AddLast(item);
        }
        while (_allItems.Count > MaxEntries)
        {
            _allItems.RemoveFirst();
        }

        OnPropertyChanged(nameof(TotalCaptured));

        // Append only the newly-captured items that pass the current filter, and trim the
        // visible list in lockstep with the backing store so the two never drift apart.
        foreach (var item in dequeued)
        {
            if (MatchesFilter(item))
            {
                LogEntries.Add(item);
            }
        }

        while (LogEntries.Count > MaxEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Rebuilds the visible list from the full backing store using the current filter.
    /// Called whenever the filter text or direction changes so existing captured traffic
    /// is re-projected, not just future messages.
    /// </summary>
    private void ReapplyFilter()
    {
        LogEntries.Clear();
        foreach (var item in _allItems)
        {
            if (MatchesFilter(item))
            {
                LogEntries.Add(item);
            }
        }
    }

    private bool MatchesFilter(MqttLogItem item)
    {
        if (!string.IsNullOrEmpty(FilterText))
        {
            bool topicMatch = item.Topic.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
            bool payloadMatch = item.PayloadJson.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
            if (!topicMatch && !payloadMatch) return false;
        }

        if (SelectedDirection != "全部")
        {
            bool match = SelectedDirection switch
            {
                "接收 (IN)" => item.Direction == MqttDirection.Incoming,
                "发送 (OUT)" => item.Direction == MqttDirection.Outgoing,
                "自发回显 (sent(echo))" => item.Direction == MqttDirection.SentEcho,
                _ => true
            };
            if (!match) return false;
        }

        return true;
    }

    partial void OnFilterTextChanged(string value) => ReapplyFilter();

    partial void OnSelectedDirectionChanged(string value) => ReapplyFilter();

    [RelayCommand]
    public void Clear()
    {
        LogEntries.Clear();
        _allItems.Clear();
        while (_pendingItems.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _broker.MessageIntercepted -= OnLogItemReceived;
        _client.ClientMessageLogged -= OnLogItemReceived;
        _batchTimer.Stop();
    }
}
