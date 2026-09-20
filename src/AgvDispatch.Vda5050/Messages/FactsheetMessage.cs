namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Factsheet message describing the physical and protocol capabilities of the AGV.
/// </summary>
public class FactsheetMessage : Vda5050Header
{
    public TypeSpecification TypeSpecification { get; set; } = new();

    public PhysicalParameters PhysicalParameters { get; set; } = new();

    public ProtocolLimits ProtocolLimits { get; set; } = new();

    public ProtocolFeatures ProtocolFeatures { get; set; } = new();

    public AgvGeometry AgvGeometry { get; set; } = new();

    public LoadSpecification LoadSpecification { get; set; } = new();

    public LocalizationParameters LocalizationParameters { get; set; } = new();
}
