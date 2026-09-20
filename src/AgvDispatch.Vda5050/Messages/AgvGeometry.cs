namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Geometric dimensions and physical envelope of the AGV.
/// </summary>
public class AgvGeometry
{
    public List<WheelDefinition>? WheelDefinitions { get; set; }

    public List<Envelope2D>? Envelopes2d { get; set; }
}
