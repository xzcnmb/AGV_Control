namespace AgvDispatch.Vda5050.Models;

using AgvDispatch.Vda5050.Enums;

/// <summary>
/// Represents a VDA5050 Action. Named VdaAction to avoid collision with System.Action.
/// </summary>
public class VdaAction
{
    public string ActionId { get; set; } = string.Empty;

    public string ActionType { get; set; } = string.Empty;

    public BlockingType BlockingType { get; set; } = BlockingType.NONE;

    public string? ActionDescription { get; set; }

    public List<ActionParameter>? ActionParameters { get; set; }
}
