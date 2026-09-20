namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Parameter for an action with key-value pair.
/// </summary>
public class ActionParameter
{
    public string Key { get; set; } = string.Empty;

    public object? Value { get; set; }

    public ActionParameter() { }

    public ActionParameter(string key, object? value)
    {
        Key = key;
        Value = value;
    }
}
