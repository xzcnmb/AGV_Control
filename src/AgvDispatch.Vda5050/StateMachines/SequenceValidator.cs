namespace AgvDispatch.Vda5050.StateMachines;

using AgvDispatch.Vda5050.Messages;

/// <summary>
/// Validates sequenceId rules, edge connectivity, and released-prefix invariants per VDA5050 §9.
/// </summary>
public static class SequenceValidator
{
    /// <summary>
    /// Performs comprehensive validation of an OrderMessage covering sequenceId alternating rules,
    /// edge connectivity, and the released-prefix invariant.
    /// </summary>
    public static ValidationResult Validate(OrderMessage order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var result = new ValidationResult();

        ValidateSequenceIds(order, result);
        ValidateEdgeConnectivity(order, result);
        ValidateReleasedPrefix(order, result);

        return result;
    }

    /// <summary>
    /// Validates sequenceId rules: nodes even, edges odd, strictly increasing, and interleaved.
    /// </summary>
    public static ValidationResult ValidateSequenceIds(OrderMessage order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var result = new ValidationResult();
        ValidateSequenceIds(order, result);
        return result;
    }

    /// <summary>
    /// Validates edge connectivity: edge.startNodeId == preceding node.nodeId,
    /// edge.endNodeId == succeeding node.nodeId.
    /// </summary>
    public static ValidationResult ValidateEdgeConnectivity(OrderMessage order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var result = new ValidationResult();
        ValidateEdgeConnectivity(order, result);
        return result;
    }

    /// <summary>
    /// Validates the released-prefix invariant: released elements form a contiguous prefix,
    /// no released element follows an unreleased one, and a released edge requires both endpoints released.
    /// </summary>
    public static ValidationResult ValidateReleasedPrefix(OrderMessage order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var result = new ValidationResult();
        ValidateReleasedPrefix(order, result);
        return result;
    }

    private static void ValidateSequenceIds(OrderMessage order, ValidationResult result)
    {
        if (order.Nodes.Count == 0)
        {
            result.AddError("Order must contain at least one node.");
            return;
        }

        if (order.Edges.Count != order.Nodes.Count - 1)
        {
            result.AddError($"Order must have exactly nodes.Count - 1 edges (expected {order.Nodes.Count - 1}, but got {order.Edges.Count}).");
        }

        for (int i = 0; i < order.Nodes.Count; i++)
        {
            var node = order.Nodes[i];
            if (node.SequenceId % 2 != 0)
            {
                result.AddError($"Node '{node.NodeId}' at index {i} has sequenceId {node.SequenceId}, which is not even.");
            }

            if (i < order.Edges.Count)
            {
                var edge = order.Edges[i];
                if (edge.SequenceId % 2 == 0)
                {
                    result.AddError($"Edge '{edge.EdgeId}' at index {i} has sequenceId {edge.SequenceId}, which is not odd.");
                }

                if (edge.SequenceId != node.SequenceId + 1)
                {
                    result.AddError($"Edge '{edge.EdgeId}' sequenceId ({edge.SequenceId}) does not equal preceding node '{node.NodeId}' sequenceId ({node.SequenceId}) + 1.");
                }

                if (i + 1 < order.Nodes.Count)
                {
                    var nextNode = order.Nodes[i + 1];
                    if (nextNode.SequenceId != edge.SequenceId + 1)
                    {
                        result.AddError($"Node '{nextNode.NodeId}' sequenceId ({nextNode.SequenceId}) does not equal preceding edge '{edge.EdgeId}' sequenceId ({edge.SequenceId}) + 1.");
                    }
                }
            }
        }
    }

    private static void ValidateEdgeConnectivity(OrderMessage order, ValidationResult result)
    {
        for (int i = 0; i < order.Edges.Count; i++)
        {
            var edge = order.Edges[i];

            if (i < order.Nodes.Count)
            {
                var precedingNode = order.Nodes[i];
                if (!string.Equals(edge.StartNodeId, precedingNode.NodeId, StringComparison.Ordinal))
                {
                    result.AddError($"Edge '{edge.EdgeId}' startNodeId '{edge.StartNodeId}' does not match preceding node '{precedingNode.NodeId}'.");
                }
            }

            if (i + 1 < order.Nodes.Count)
            {
                var succeedingNode = order.Nodes[i + 1];
                if (!string.Equals(edge.EndNodeId, succeedingNode.NodeId, StringComparison.Ordinal))
                {
                    result.AddError($"Edge '{edge.EdgeId}' endNodeId '{edge.EndNodeId}' does not match succeeding node '{succeedingNode.NodeId}'.");
                }
            }
        }
    }

    private static void ValidateReleasedPrefix(OrderMessage order, ValidationResult result)
    {
        bool unreleasedSeen = false;

        for (int i = 0; i < order.Nodes.Count; i++)
        {
            var node = order.Nodes[i];

            if (unreleasedSeen && node.Released)
            {
                result.AddError($"Node '{node.NodeId}' (sequenceId {node.SequenceId}) is marked released=true after an unreleased element.");
            }

            if (!node.Released)
            {
                unreleasedSeen = true;
            }

            if (i < order.Edges.Count)
            {
                var edge = order.Edges[i];

                if (unreleasedSeen && edge.Released)
                {
                    result.AddError($"Edge '{edge.EdgeId}' (sequenceId {edge.SequenceId}) is marked released=true after an unreleased element.");
                }

                if (!edge.Released)
                {
                    unreleasedSeen = true;
                }

                if (edge.Released)
                {
                    bool startReleased = node.Released;
                    bool endReleased = (i + 1 < order.Nodes.Count) && order.Nodes[i + 1].Released;

                    if (!startReleased || !endReleased)
                    {
                        result.AddError($"Edge '{edge.EdgeId}' is released, but its endpoint nodes (start: {startReleased}, end: {endReleased}) are not both released.");
                    }
                }
            }
        }
    }
}
