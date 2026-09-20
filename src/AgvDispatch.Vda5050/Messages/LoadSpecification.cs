namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Specification of load handling capabilities.
/// </summary>
public class LoadSpecification
{
    public List<string>? LoadPositions { get; set; }

    public List<LoadSet>? LoadSets { get; set; }

    public double? MaxLoadMass { get; set; }
}
