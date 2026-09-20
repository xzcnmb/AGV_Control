namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Trajectory of an edge defined as a B-spline curve.
/// </summary>
public class Trajectory
{
    public int Degree { get; set; } = 1;

    public List<double> KnotVector { get; set; } = new();

    public List<ControlPoint> ControlPoints { get; set; } = new();
}
