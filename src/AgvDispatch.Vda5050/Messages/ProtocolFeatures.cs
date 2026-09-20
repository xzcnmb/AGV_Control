namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Protocol features and supported actions of the AGV.
/// </summary>
public class ProtocolFeatures
{
    public List<AgvActionSpecification> AgvActions { get; set; } = new()
    {
        new AgvActionSpecification
        {
            ActionType = "charge",
            ActionDescription = "Recharge AGV battery",
            ActionScopes = new() { "NODE", "INSTANT" }
        },
        new AgvActionSpecification
        {
            ActionType = "startPause",
            ActionDescription = "Pause AGV motion",
            ActionScopes = new() { "INSTANT" }
        },
        new AgvActionSpecification
        {
            ActionType = "stopPause",
            ActionDescription = "Resume AGV motion",
            ActionScopes = new() { "INSTANT" }
        },
        new AgvActionSpecification
        {
            ActionType = "cancelOrder",
            ActionDescription = "Cancel current order",
            ActionScopes = new() { "INSTANT" }
        },
        new AgvActionSpecification
        {
            ActionType = "stateRequest",
            ActionDescription = "Request immediate state report",
            ActionScopes = new() { "INSTANT" }
        },
        new AgvActionSpecification
        {
            ActionType = "factsheetRequest",
            ActionDescription = "Request factsheet",
            ActionScopes = new() { "INSTANT" }
        }
    };

    public List<string>? OptionalParameters { get; set; }
}
