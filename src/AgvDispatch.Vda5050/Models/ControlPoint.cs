namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Control point for a trajectory B-spline.
/// </summary>
public class ControlPoint
{
    public double X { get; set; }

    public double Y { get; set; }

    public double? Weight { get; set; }

    public double? Orientation { get; set; }
}
