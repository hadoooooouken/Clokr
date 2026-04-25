namespace Clokr.Models;

/// <summary>
/// Represents power settings for a single power source (AC or DC).
/// </summary>
public class PowerProfile
{
    /// <summary>Processor performance boost mode (0-6).</summary>
    public BoostMode BoostMode { get; set; } = BoostMode.Aggressive;

    /// <summary>Maximum frequency for Efficiency Class 0 in MHz. 0 = no limit.</summary>
    public int Class0Mhz { get; set; }

    /// <summary>Maximum frequency for Efficiency Class 1 in MHz. 0 = no limit.</summary>
    public int Class1Mhz { get; set; }

    /// <summary>Maximum frequency for Efficiency Class 2 in MHz. 0 = no limit.</summary>
    public int Class2Mhz { get; set; }

    public PowerProfile Clone() => new()
    {
        BoostMode = BoostMode,
        Class0Mhz = Class0Mhz,
        Class1Mhz = Class1Mhz,
        Class2Mhz = Class2Mhz
    };
}
