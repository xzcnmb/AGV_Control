namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Definition of a load set supported by the AGV.
/// </summary>
public class LoadSet
{
    public string? SetName { get; set; }

    public string? LoadType { get; set; }

    public List<string>? LoadPositions { get; set; }
}
