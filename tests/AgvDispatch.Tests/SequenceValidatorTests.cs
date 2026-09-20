namespace AgvDispatch.Tests;

using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;
using AgvDispatch.Vda5050.StateMachines;

public class SequenceValidatorTests
{
    [Fact]
    public void Validate_ValidLinearOrder_ReturnsSuccessWithNoErrors()
    {
        // 3 nodes (seq 0, 2, 4) and 2 edges (seq 1, 3), all released
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 3, releasedNodeCount: 3);

        var result = SequenceValidator.Validate(order);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ValidLinearOrderWithUnreleasedHorizon_ReturnsSuccess()
    {
        // 4 nodes (0, 2, 4, 6), 3 edges (1, 3, 5). Base released up to index 2 (node 0, 1; edge 0)
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 4, releasedNodeCount: 2);

        var result = SequenceValidator.Validate(order);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NodeWithOddSequenceId_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2);
        order.Nodes[1].SequenceId = 3; // Should be even (e.g. 2)

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not even", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EdgeWithEvenSequenceId_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2);
        order.Edges[0].SequenceId = 2; // Should be odd (1)
        order.Nodes[1].SequenceId = 4;

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not odd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SequenceIdNonStrictlyInterleaved_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2);
        order.Nodes[0].SequenceId = 0;
        order.Edges[0].SequenceId = 3; // gap: should be node.SequenceId + 1 = 1
        order.Nodes[1].SequenceId = 4;

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not equal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EdgeCountNotNodesMinusOne_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 3, releasedNodeCount: 3);
        order.Edges.RemoveAt(1); // Now 3 nodes, but only 1 edge

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("nodes.Count - 1 edges", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EmptyNodes_ReturnsInvalid()
    {
        var order = new OrderMessage();

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one node", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EdgeStartNodeIdMismatch_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2);
        order.Edges[0].StartNodeId = "wrong_start_node";

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("startNodeId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EdgeEndNodeIdMismatch_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2);
        order.Edges[0].EndNodeId = "wrong_end_node";

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("endNodeId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReleasedPrefixViolation_ReleasedNodeAfterUnreleasedNode_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 3, releasedNodeCount: 1);
        // node 0 released=true, edge 0 released=false, node 1 released=false, edge 1 released=false, node 2 released=false
        // Now turn node 2 released=true after unreleased node 1
        order.Nodes[2].Released = true;

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("after an unreleased element", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ReleasedPrefixViolation_ReleasedEdgeWhoseEndpointIsUnreleased_ReturnsInvalid()
    {
        var order = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 1);
        // node 0 released=true, node 1 released=false, edge 0 released=false
        // Force edge 0 to released=true while its end node 1 is unreleased
        order.Edges[0].Released = true;

        var result = SequenceValidator.Validate(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("endpoint nodes", StringComparison.OrdinalIgnoreCase));
    }
}
