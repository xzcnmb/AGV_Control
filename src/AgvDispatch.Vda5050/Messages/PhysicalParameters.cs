namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Physical parameters of the AGV.
/// </summary>
public class PhysicalParameters
{
    public double? SpeedMin { get; set; }

    public double? SpeedMax { get; set; }

    public double? AccelerationMax { get; set; }

    public double? DecelerationMax { get; set; }

    public double? HeightMin { get; set; }

    public double? HeightMax { get; set; }

    public double? Width { get; set; }

    public double? Length { get; set; }
}
