namespace Clokr.Models;

/// <summary>
/// Processor Performance Boost Mode values.
/// Registry: HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\
///   54533251-82be-4824-96c1-47b60b740d00\be337238-0d82-4146-a960-4f3749d470c7
/// </summary>
public enum BoostMode
{
    Disabled = 0,
    Enabled = 1,
    Aggressive = 2,
    EfficientEnabled = 3,
    EfficientAggressive = 4,
    AggressiveAtGuaranteed = 5,
    EfficientAggressiveAtGuaranteed = 6
}

public static class BoostModeExtensions
{
    public static string ToDisplayString(this BoostMode mode) => mode switch
    {
        BoostMode.Disabled => "Disabled",
        BoostMode.Enabled => "Enabled",
        BoostMode.Aggressive => "Aggressive",
        BoostMode.EfficientEnabled => "Efficient Enabled",
        BoostMode.EfficientAggressive => "Efficient Aggressive",
        BoostMode.AggressiveAtGuaranteed => "Aggressive At Guaranteed",
        BoostMode.EfficientAggressiveAtGuaranteed => "Efficient Aggressive At Guaranteed",
        _ => mode.ToString()
    };
}
