namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Key-value reference associated with an error.
/// </summary>
public class ErrorReference
{
    public string ReferenceKey { get; set; } = string.Empty;

    public string ReferenceValue { get; set; } = string.Empty;

    public ErrorReference() { }

    public ErrorReference(string referenceKey, string referenceValue)
    {
        ReferenceKey = referenceKey;
        ReferenceValue = referenceValue;
    }
}
