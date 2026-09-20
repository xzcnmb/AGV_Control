namespace AgvDispatch.Vda5050.StateMachines;

/// <summary>
/// Result of evaluating an incoming order against the current order state per VDA5050 §12.
/// </summary>
public enum AcceptanceResult
{
    /// <summary>
    /// New orderId: accept and replace current order.
    /// </summary>
    Accept,

    /// <summary>
    /// Same orderId and same orderUpdateId: duplicate message, ignore.
    /// </summary>
    Ignore,

    /// <summary>
    /// Same orderId with lower orderUpdateId: out-of-order update, reject.
    /// </summary>
    RejectLowerUpdate,

    /// <summary>
    /// Same orderId with higher orderUpdateId and matching stitching node: accept update.
    /// </summary>
    StitchOk,

    /// <summary>
    /// Same orderId with higher orderUpdateId but stitching node does not match: reject.
    /// </summary>
    StitchMismatch
}
