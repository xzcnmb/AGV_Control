namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Type specification of the AGV.
/// </summary>
public class TypeSpecification
{
    public string SeriesName { get; set; } = "AgvSimSeries";

    public string? SeriesDescription { get; set; }

    public string AgvKinematics { get; set; } = "DIFF";

    public string AgvClass { get; set; } = "CARRIER";

    public int? MaxArrayLens { get; set; }

    public int? MaxStringLens { get; set; }
}
