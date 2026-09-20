namespace AgvDispatch.Vda5050.Messages;

using System.Text.Json.Serialization;
using AgvDispatch.Vda5050.Models;

/// <summary>
/// Instant actions message dispatched to an AGV for immediate execution.
/// </summary>
public class InstantActionsMessage : Vda5050Header
{
    [JsonPropertyName("actions")]
    public List<VdaAction> Actions { get; set; } = new();
}
