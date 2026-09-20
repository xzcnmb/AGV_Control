namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Specification of an action supported by the AGV.
/// </summary>
public class AgvActionSpecification
{
    public string ActionType { get; set; } = string.Empty;

    public string? ActionDescription { get; set; }

    public List<string>? ActionScopes { get; set; }

    public string? ResultDescription { get; set; }
}
