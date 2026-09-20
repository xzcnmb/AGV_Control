namespace AgvDispatch.Vda5050.StateMachines;

using AgvDispatch.Vda5050.Messages;

/// <summary>
/// Implements the order acceptance state machine logic according to VDA5050 §12.
/// </summary>
public static class OrderAcceptance
{
    /// <summary>
    /// Evaluates an incoming order against the currently accepted order and AGV state.
    /// </summary>
    /// <param name="current">The currently accepted order, or null if no active order exists.</param>
    /// <param name="incoming">The newly received order message.</param>
    /// <param name="lastNodeId">The nodeId of the last node traversed or the expected stitching node.</param>
    /// <param name="lastNodeSequenceId">The sequenceId of the last node traversed or the expected stitching node.</param>
    /// <returns>An AcceptanceResult indicating whether to accept, ignore, reject, or stitch the incoming order.</returns>
    public static AcceptanceResult Evaluate(
        OrderMessage? current,
        OrderMessage incoming,
        string lastNodeId = "",
        uint lastNodeSequenceId = 0)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        // Case 4: No active order exists -> Accept new order
        if (current == null || string.IsNullOrEmpty(current.OrderId))
        {
            return AcceptanceResult.Accept;
        }

        // Case 4: New orderId -> Accept and replace current order
        if (!string.Equals(incoming.OrderId, current.OrderId, StringComparison.Ordinal))
        {
            return AcceptanceResult.Accept;
        }

        // Same orderId:
        // Case 2: Same orderUpdateId -> Ignore (deduplication)
        if (incoming.OrderUpdateId == current.OrderUpdateId)
        {
            return AcceptanceResult.Ignore;
        }

        // Case 3: Lower orderUpdateId -> Reject (out-of-order)
        if (incoming.OrderUpdateId < current.OrderUpdateId)
        {
            return AcceptanceResult.RejectLowerUpdate;
        }

        // Case 5: Higher orderUpdateId -> Check stitching node
        // The first base node of incoming must match the last base node of current.
        var incomingFirstBaseNode = incoming.Nodes.FirstOrDefault(n => n.Released)
                                 ?? incoming.Nodes.FirstOrDefault();

        if (incomingFirstBaseNode == null)
        {
            return AcceptanceResult.StitchMismatch;
        }

        // Canonical stitching node: the last released node of the current order
        var currentLastBaseNode = current.Nodes.LastOrDefault(n => n.Released);

        if (currentLastBaseNode != null)
        {
            if (string.Equals(incomingFirstBaseNode.NodeId, currentLastBaseNode.NodeId, StringComparison.Ordinal)
                && incomingFirstBaseNode.SequenceId == currentLastBaseNode.SequenceId)
            {
                return AcceptanceResult.StitchOk;
            }
        }

        // Fallback or explicit check against provided lastNodeId and lastNodeSequenceId
        if (!string.IsNullOrEmpty(lastNodeId))
        {
            if (string.Equals(incomingFirstBaseNode.NodeId, lastNodeId, StringComparison.Ordinal)
                && incomingFirstBaseNode.SequenceId == lastNodeSequenceId)
            {
                return AcceptanceResult.StitchOk;
            }
        }

        return AcceptanceResult.StitchMismatch;
    }

    /// <summary>
    /// Overload returning OrderAcceptanceResult alias.
    /// </summary>
    public static OrderAcceptanceResult EvaluateOrder(
        OrderMessage? current,
        OrderMessage incoming,
        string lastNodeId = "",
        uint lastNodeSequenceId = 0)
    {
        return (OrderAcceptanceResult)Evaluate(current, incoming, lastNodeId, lastNodeSequenceId);
    }
}
