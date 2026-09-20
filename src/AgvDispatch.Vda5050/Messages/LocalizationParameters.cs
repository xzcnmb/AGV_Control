namespace AgvDispatch.Vda5050.Messages;

/// <summary>
/// Localization parameters and supported methods of the AGV.
/// </summary>
public class LocalizationParameters
{
    public List<string>? SupportedTypes { get; set; } = new() { "NATURAL", "REFLECTOR" };
}
