namespace AgvDispatch.MasterControl.Models;

public enum OrderCompletionStatus
{
    CREATED,
    DISPATCHED,
    ACCEPTED,
    RUNNING,
    COMPLETED,
    CANCELLED,
    FAILED
}

public class OrderRecord
{
    public string OrderId { get; set; } = string.Empty;
    public string AgvSerial { get; set; } = string.Empty;
    public uint OrderUpdateId { get; set; }
    public string Status { get; set; } = OrderCompletionStatus.CREATED.ToString();
    public string PayloadJson { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    public string DispatchedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}

public class OrderEventRecord
{
    public long Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string AgvSerial { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Ts { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    public string PayloadJson { get; set; } = string.Empty;
}

public class AgvEventRecord
{
    public long Id { get; set; }
    public string AgvSerial { get; set; } = string.Empty;
    public string Ts { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

public class ErrorRecord
{
    public long Id { get; set; }
    public string AgvSerial { get; set; } = string.Empty;
    public string Ts { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    public string ErrorType { get; set; } = string.Empty;
    public string ErrorLevel { get; set; } = string.Empty;
    public string ErrorDescription { get; set; } = string.Empty;
    public string ErrorReferencesJson { get; set; } = string.Empty;
}
