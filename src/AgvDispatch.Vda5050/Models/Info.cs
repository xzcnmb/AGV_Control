namespace AgvDispatch.Vda5050.Models;

using AgvDispatch.Vda5050.Enums;

/// <summary>
/// Represents an informational message reported by the AGV.
/// </summary>
public class Info
{
    public string InfoType { get; set; } = string.Empty;

    public InfoLevel InfoLevel { get; set; } = InfoLevel.INFO;

    public string? InfoDescription { get; set; }

    public List<InfoReference>? InfoReferences { get; set; }
}
