namespace AgvDispatch.Vda5050.Models;

/// <summary>
/// Represents the battery state of the AGV.
/// </summary>
public class BatteryState
{
    public double BatteryCharge { get; set; }

    public bool Charging { get; set; }

    public double? BatteryVoltage { get; set; }

    public double? BatteryHealth { get; set; }

    public double? Reach { get; set; }
}
