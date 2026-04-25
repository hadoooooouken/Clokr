namespace Clokr.Models;

/// <summary>
/// Defines the layout of efficiency classes exposed by the OS for this processor.
/// </summary>
public enum CpuTopology
{
    Unknown,

    /// <summary>Single efficiency class (Class 0). Example: AMD Ryzen, older Intel CPUs.</summary>
    Homogeneous,

    /// <summary>Two efficiency classes (Class 1 = P, Class 0 = E). Example: Alder Lake, Raptor Lake.</summary>
    Hybrid,

    /// <summary>Three efficiency classes (Class 2 = P, Class 1 = E, Class 0 = LPE). Example: Meteor Lake, Core Ultra.</summary>
    TriHybrid
}
