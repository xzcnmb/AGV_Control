namespace AgvDispatch.Vda5050.Models;

using AgvDispatch.Vda5050.Enums;

/// <summary>
/// Represents an error or warning reported by the AGV.
/// </summary>
public class Error
{
    public string ErrorType { get; set; } = string.Empty;

    public ErrorLevel ErrorLevel { get; set; } = ErrorLevel.WARNING;

    public string? ErrorDescription { get; set; }

    public string? ErrorHint { get; set; }

    public List<ErrorReference>? ErrorReferences { get; set; }
}
