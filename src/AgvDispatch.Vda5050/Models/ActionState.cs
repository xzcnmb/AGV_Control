namespace AgvDispatch.Vda5050.Models;

using AgvDispatch.Vda5050.Enums;

/// <summary>
/// Represents the execution state of an action.
/// </summary>
public class ActionState
{
    public string ActionId { get; set; } = string.Empty;

    public string? ActionType { get; set; }

    public string? ActionDescription { get; set; }

    public ActionStatus ActionStatus { get; set; } = ActionStatus.WAITING;

    public string? ResultDescription { get; set; }
}
