namespace AgvDispatch.Vda5050.Messages;

using AgvDispatch.Vda5050.Models;

/// <summary>
/// Order message dispatched to an AGV containing nodes and edges to traverse.
/// </summary>
public class OrderMessage : Vda5050Header
{
    public string OrderId { get; set; } = string.Empty;

    public uint OrderUpdateId { get; set; }

    public string? ZoneSetId { get; set; }

    public List<Node> Nodes { get; set; } = new();

    public List<Edge> Edges { get; set; } = new();
}
