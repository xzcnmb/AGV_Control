namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Key-value reference associated with an informational message.
/// </summary>
public class InfoReference
{
    public string ReferenceKey { get; set; } = string.Empty;

    public string ReferenceValue { get; set; } = string.Empty;

    public InfoReference() { }

    public InfoReference(string referenceKey, string referenceValue)
    {
        ReferenceKey = referenceKey;
        ReferenceValue = referenceValue;
    }
}
