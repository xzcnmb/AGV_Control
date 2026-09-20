using System.IO;
using Microsoft.Data.Sqlite;
using AgvDispatch.MasterControl.Models;

namespace AgvDispatch.MasterControl.Services;

/// <summary>
/// SQLite task repository (§18) using a single long-lived connection and serialized writes.
/// </summary>
public class TaskRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public TaskRepository(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            dbPath = Path.Combine(baseDir, "master_control.db");
        }

        _connectionString = $"Data Source={dbPath};";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;
                PRAGMA foreign_keys=ON;

                CREATE TABLE IF NOT EXISTS orders (
                    order_id TEXT PRIMARY KEY,
                    agv_serial TEXT,
                    order_update_id INT,
                    status TEXT,
                    payload_json TEXT,
                    created_at TEXT,
                    dispatched_at TEXT,
                    updated_at TEXT
                );

                CREATE TABLE IF NOT EXISTS order_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    order_id TEXT,
                    agv_serial TEXT,
                    event_type TEXT,
                    ts TEXT,
                    payload_json TEXT
                );

                CREATE TABLE IF NOT EXISTS agv_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agv_serial TEXT,
                    ts TEXT,
                    event_type TEXT,
                    payload_json TEXT
                );

                CREATE TABLE IF NOT EXISTS errors (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agv_serial TEXT,
                    ts TEXT,
                    error_type TEXT,
                    error_level TEXT,
                    error_description TEXT,
                    error_references_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_order_events_order_id_ts ON order_events(order_id, ts);
                CREATE INDEX IF NOT EXISTS idx_order_events_agv_serial_ts ON order_events(agv_serial, ts);
                CREATE INDEX IF NOT EXISTS idx_agv_events_agv_serial_ts ON agv_events(agv_serial, ts);
                CREATE INDEX IF NOT EXISTS idx_errors_agv_serial_ts ON errors(agv_serial, ts);
            ";
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertOrder(OrderRecord order)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO orders (order_id, agv_serial, order_update_id, status, payload_json, created_at, dispatched_at, updated_at)
                VALUES ($order_id, $agv_serial, $order_update_id, $status, $payload_json, $created_at, $dispatched_at, $updated_at);
            ";
            cmd.Parameters.AddWithValue("$order_id", order.OrderId);
            cmd.Parameters.AddWithValue("$agv_serial", order.AgvSerial);
            cmd.Parameters.AddWithValue("$order_update_id", order.OrderUpdateId);
            cmd.Parameters.AddWithValue("$status", order.Status);
            cmd.Parameters.AddWithValue("$payload_json", order.PayloadJson);
            cmd.Parameters.AddWithValue("$created_at", order.CreatedAt);
            cmd.Parameters.AddWithValue("$dispatched_at", order.DispatchedAt ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$updated_at", order.UpdatedAt);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateOrderStatus(string orderId, string status, string? updatedAt = null)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE orders
                SET status = $status, updated_at = $updated_at
                WHERE order_id = $order_id;
            ";
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$updated_at", updatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            cmd.Parameters.AddWithValue("$order_id", orderId);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateOrderDispatched(string orderId, string dispatchedAt)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE orders
                SET status = 'DISPATCHED', dispatched_at = $dispatched_at, updated_at = $dispatched_at
                WHERE order_id = $order_id;
            ";
            cmd.Parameters.AddWithValue("$dispatched_at", dispatchedAt);
            cmd.Parameters.AddWithValue("$order_id", orderId);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertOrderEvent(string orderId, string agvSerial, string eventType, string payloadJson)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO order_events (order_id, agv_serial, event_type, ts, payload_json)
                VALUES ($order_id, $agv_serial, $event_type, $ts, $payload_json);
            ";
            cmd.Parameters.AddWithValue("$order_id", orderId);
            cmd.Parameters.AddWithValue("$agv_serial", agvSerial);
            cmd.Parameters.AddWithValue("$event_type", eventType);
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            cmd.Parameters.AddWithValue("$payload_json", payloadJson);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertAgvEvent(string agvSerial, string eventType, string payloadJson)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO agv_events (agv_serial, ts, event_type, payload_json)
                VALUES ($agv_serial, $ts, $event_type, $payload_json);
            ";
            cmd.Parameters.AddWithValue("$agv_serial", agvSerial);
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            cmd.Parameters.AddWithValue("$event_type", eventType);
            cmd.Parameters.AddWithValue("$payload_json", payloadJson);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertError(string agvSerial, string errorType, string errorLevel, string description, string referencesJson)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO errors (agv_serial, ts, error_type, error_level, error_description, error_references_json)
                VALUES ($agv_serial, $ts, $error_type, $error_level, $description, $references_json);
            ";
            cmd.Parameters.AddWithValue("$agv_serial", agvSerial);
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            cmd.Parameters.AddWithValue("$error_type", errorType);
            cmd.Parameters.AddWithValue("$error_level", errorLevel);
            cmd.Parameters.AddWithValue("$description", description);
            cmd.Parameters.AddWithValue("$references_json", referencesJson);
            cmd.ExecuteNonQuery();
        }
    }

    public List<OrderRecord> GetOrders(int limit = 50)
    {
        lock (_lock)
        {
            var list = new List<OrderRecord>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT order_id, agv_serial, order_update_id, status, payload_json, created_at, dispatched_at, updated_at FROM orders ORDER BY created_at DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new OrderRecord
                {
                    OrderId = reader.GetString(0),
                    AgvSerial = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    OrderUpdateId = reader.IsDBNull(2) ? 0 : (uint)reader.GetInt64(2),
                    Status = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    PayloadJson = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    CreatedAt = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    DispatchedAt = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    UpdatedAt = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                });
            }
            return list;
        }
    }

    public List<OrderEventRecord> GetOrderEvents(string orderId, int limit = 100)
    {
        lock (_lock)
        {
            var list = new List<OrderEventRecord>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, order_id, agv_serial, event_type, ts, payload_json FROM order_events WHERE order_id = $order_id ORDER BY id DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$order_id", orderId);
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new OrderEventRecord
                {
                    Id = reader.GetInt64(0),
                    OrderId = reader.GetString(1),
                    AgvSerial = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EventType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Ts = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    PayloadJson = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                });
            }
            return list;
        }
    }

    public List<AgvEventRecord> GetAgvEvents(string agvSerial, int limit = 100)
    {
        lock (_lock)
        {
            var list = new List<AgvEventRecord>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, agv_serial, ts, event_type, payload_json FROM agv_events WHERE agv_serial = $agv_serial ORDER BY id DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$agv_serial", agvSerial);
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AgvEventRecord
                {
                    Id = reader.GetInt64(0),
                    AgvSerial = reader.GetString(1),
                    Ts = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EventType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    PayloadJson = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                });
            }
            return list;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _connection.Dispose();
        }
    }
}
