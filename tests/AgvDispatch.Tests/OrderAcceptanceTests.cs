namespace AgvDispatch.Tests;

using AgvDispatch.Vda5050.Messages;
using AgvDispatch.Vda5050.Models;
using AgvDispatch.Vda5050.StateMachines;

public class OrderAcceptanceTests
{
    [Fact]
    public void Evaluate_WhenCurrentIsNull_ReturnsAccept()
    {
        var incoming = Vda5050TestFactory.CreateMinimalOrder("order-001", 0);

        var result = OrderAcceptance.Evaluate(null, incoming);
        Assert.Equal(AcceptanceResult.Accept, result);

        var aliasResult = OrderAcceptance.EvaluateOrder(null, incoming);
        Assert.Equal(OrderAcceptanceResult.Accept, aliasResult);
    }

    [Fact]
    public void Evaluate_WhenCurrentOrderIdIsEmpty_ReturnsAccept()
    {
        var current = new OrderMessage { OrderId = string.Empty };
        var incoming = Vda5050TestFactory.CreateMinimalOrder("order-001", 0);

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.Accept, result);
    }

    [Fact]
    public void Evaluate_WhenDifferentOrderId_ReturnsAccept()
    {
        var current = Vda5050TestFactory.CreateMinimalOrder("order-001", 0);
        var incoming = Vda5050TestFactory.CreateMinimalOrder("order-002", 0);

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.Accept, result);
    }

    [Fact]
    public void Evaluate_WhenSameOrderIdAndSameOrderUpdateId_ReturnsIgnore()
    {
        var current = Vda5050TestFactory.CreateMinimalOrder("order-001", 1);
        var incoming = Vda5050TestFactory.CreateMinimalOrder("order-001", 1);

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.Ignore, result);
    }

    [Fact]
    public void Evaluate_WhenSameOrderIdAndLowerOrderUpdateId_ReturnsRejectLowerUpdate()
    {
        var current = Vda5050TestFactory.CreateMinimalOrder("order-001", 5);
        var incoming = Vda5050TestFactory.CreateMinimalOrder("order-001", 4);

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.RejectLowerUpdate, result);
    }

    [Fact]
    public void Evaluate_WhenSameOrderIdAndHigherOrderUpdateId_AndStitchNodeMatchesCurrentLastBaseNode_ReturnsStitchOk()
    {
        // Current has nodes seq 0, 2 released. Last released node is node_1 (seq 2).
        var current = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2, orderId: "order-001", orderUpdateId: 0);

        // Incoming has orderUpdateId 1. Its first base node is node_1 (seq 2, released).
        var incoming = new OrderMessage
        {
            OrderId = "order-001",
            OrderUpdateId = 1,
            Nodes = new List<Node>
            {
                new() { NodeId = "node_1", SequenceId = 2, Released = true },
                new() { NodeId = "node_2", SequenceId = 4, Released = true }
            },
            Edges = new List<Edge>
            {
                new() { EdgeId = "edge_1", SequenceId = 3, StartNodeId = "node_1", EndNodeId = "node_2", Released = true }
            }
        };

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.StitchOk, result);
    }

    [Fact]
    public void Evaluate_WhenSameOrderIdAndHigherOrderUpdateId_AndExplicitLastNodeMatches_ReturnsStitchOk()
    {
        var current = new OrderMessage
        {
            OrderId = "order-001",
            OrderUpdateId = 0,
            Nodes = new List<Node>() // no released nodes in current
        };

        var incoming = new OrderMessage
        {
            OrderId = "order-001",
            OrderUpdateId = 1,
            Nodes = new List<Node>
            {
                new() { NodeId = "stitch_node", SequenceId = 10, Released = true }
            }
        };

        var result = OrderAcceptance.Evaluate(current, incoming, lastNodeId: "stitch_node", lastNodeSequenceId: 10);
        Assert.Equal(AcceptanceResult.StitchOk, result);
    }

    [Fact]
    public void Evaluate_WhenSameOrderIdAndHigherOrderUpdateId_AndStitchNodeIdDiffers_ReturnsStitchMismatch()
    {
        var current = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2, orderId: "order-001", orderUpdateId: 0);

        var incoming = new OrderMessage
        {
            OrderId = "order-001",
            OrderUpdateId = 1,
            Nodes = new List<Node>
            {
                new() { NodeId = "wrong_node", SequenceId = 2, Released = true }
            }
        };

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.StitchMismatch, result);
    }

    [Fact]
    public void Evaluate_WhenSameOrderIdAndHigherOrderUpdateId_AndStitchSequenceIdDiffers_ReturnsStitchMismatch()
    {
        var current = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2, orderId: "order-001", orderUpdateId: 0);

        var incoming = new OrderMessage
        {
            OrderId = "order-001",
            OrderUpdateId = 1,
            Nodes = new List<Node>
            {
                new() { NodeId = "node_1", SequenceId = 4, Released = true } // current node_1 has seq 2
            }
        };

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.StitchMismatch, result);
    }

    [Fact]
    public void Evaluate_WhenIncomingHasNoNodes_ReturnsStitchMismatch()
    {
        var current = Vda5050TestFactory.CreateLinearOrder(nodeCount: 2, releasedNodeCount: 2, orderId: "order-001", orderUpdateId: 0);

        var incoming = new OrderMessage
        {
            OrderId = "order-001",
            OrderUpdateId = 1,
            Nodes = new List<Node>()
        };

        var result = OrderAcceptance.Evaluate(current, incoming);
        Assert.Equal(AcceptanceResult.StitchMismatch, result);
    }
}
