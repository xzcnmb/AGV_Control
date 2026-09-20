namespace AgvDispatch.Vda5050.Messages;

using AgvDispatch.Vda5050.Models;

/// <summary>
/// High-frequency visualization message for AGV position and velocity.
/// </summary>
public class VisualizationMessage : Vda5050Header
{
    public AgvPosition? AgvPosition { get; set; }

    public Velocity? Velocity { get; set; }
}
