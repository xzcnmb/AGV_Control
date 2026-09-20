namespace AgvDispatch.Vda5050.Messages;

using AgvDispatch.Vda5050.Models;

/// <summary>
/// Definition of a wheel on the AGV.
/// </summary>
public class WheelDefinition
{
    public string? Type { get; set; }

    public bool? IsActiveDriven { get; set; }

    public bool? IsActiveSteered { get; set; }

    public NodePosition? Position { get; set; }
}
