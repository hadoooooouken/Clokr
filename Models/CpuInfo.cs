namespace Clokr.Models;

public class CpuInfo
{
    public int PhysicalCores { get; set; }
    public int LogicalProcessors { get; set; }
    public int CoreClassCount { get; set; } = 1;
    public int P_Cores { get; set; }
    public int E_Cores { get; set; }
    public int LPE_Cores { get; set; }
    public int L2CacheMB { get; set; }
    public int L3CacheMB { get; set; }
    public double BaseFrequencyGHz { get; set; }
    public string BiosInfo { get; set; } = "Unknown";
    public string Motherboard { get; set; } = "Unknown";
    public string RamInfo { get; set; } = "Unknown";
}
