namespace AgvDispatch.Simulator.Services;

using AgvDispatch.Vda5050.Models;

/// <summary>
/// Simulates battery dynamics per VDA5050 §14:
/// - Drains while driving (-0.05 %/s default)
/// - Tiny idle drain (-0.001 %/s default)
/// - Recovers while charging (+1.0 %/s default)
/// </summary>
public class BatterySim
{
    private readonly object _lock = new();

    private double _batteryCharge = 100.0;
    private bool _charging;

    public double DrivingDrainRatePerSec { get; set; } = 0.05; // -0.05 %/s
    public double IdleDrainRatePerSec { get; set; } = 0.001;   // -0.001 %/s
    public double ChargeRatePerSec { get; set; } = 1.0;        // +1.0 %/s

    public double BatteryCharge
    {
        get
        {
            lock (_lock) return _batteryCharge;
        }
        set
        {
            lock (_lock)
            {
                _batteryCharge = Math.Clamp(value, 0.0, 100.0);
            }
        }
    }

    public bool Charging
    {
        get
        {
            lock (_lock) return _charging;
        }
        set
        {
            lock (_lock) _charging = value;
        }
    }

    public BatteryState GetSnapshot()
    {
        lock (_lock)
        {
            return new BatteryState
            {
                BatteryCharge = Math.Round(_batteryCharge, 2),
                Charging = _charging,
                BatteryVoltage = 24.0 + (_batteryCharge / 100.0) * 4.0, // approx 24V - 28V
                BatteryHealth = 100.0,
                Reach = Math.Round(_batteryCharge * 100.0, 0) // ~100m per 1% charge
            };
        }
    }

    /// <summary>
    /// Updates battery state based on elapsed time and driving state.
    /// </summary>
    public void Tick(double elapsedSeconds, bool isDriving)
    {
        lock (_lock)
        {
            if (_charging)
            {
                _batteryCharge += ChargeRatePerSec * elapsedSeconds;
            }
            else if (isDriving)
            {
                _batteryCharge -= DrivingDrainRatePerSec * elapsedSeconds;
            }
            else
            {
                _batteryCharge -= IdleDrainRatePerSec * elapsedSeconds;
            }

            _batteryCharge = Math.Clamp(_batteryCharge, 0.0, 100.0);
        }
    }
}
