using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Clokr.Models;

namespace Clokr.Services;

/// <summary>
/// Wraps powercfg.exe commands to read and write processor power settings.
/// 
/// PROCFREQMAX  (75b0ae3f-bce0-45a7-8c89-c9611c25e100) - E-core max frequency (MHz)
/// PROCFREQMAX1 (75b0ae3f-bce0-45a7-8c89-c9611c25e101) - P-core max frequency (MHz)
/// PERFBOOSTMODE (be337238-0d82-4146-a960-4f3749d470c7) - Boost mode (0-6)
/// </summary>
public partial class PowerCfgService
{
    private const string SubProcessor = "SUB_PROCESSOR";
    private const string ProcFreqMax = "PROCFREQMAX";    // Class 0
    private const string ProcFreqMax1 = "PROCFREQMAX1";  // Class 1
    private const string ProcFreqMax2 = "PROCFREQMAX2";  // Class 2
    private const string PerfBoostMode = "PERFBOOSTMODE";

    // Regex to parse "Current AC/DC Power Setting Index: 0x00000002"
    [GeneratedRegex(@"Current\s+AC\s+Power\s+Setting\s+Index:\s+0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AcValueRegex();

    [GeneratedRegex(@"Current\s+DC\s+Power\s+Setting\s+Index:\s+0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DcValueRegex();

    /// <summary>
    /// Reads current power settings from the active power scheme.
    /// Returns (acProfile, dcProfile).
    /// </summary>
    public (PowerProfile Ac, PowerProfile Dc) ReadCurrentSettings()
    {
        var ac = new PowerProfile();
        var dc = new PowerProfile();

        // Read Class 0 frequency
        var freqMaxOutput = RunPowerCfg($"/query SCHEME_CURRENT {SubProcessor} {ProcFreqMax}");
        (ac.Class0Mhz, dc.Class0Mhz) = ParseAcDcValues(freqMaxOutput);

        // Read Class 1 frequency (might not exist on AMD/single-class CPUs)
        try
        {
            var freqMax1Output = RunPowerCfg($"/query SCHEME_CURRENT {SubProcessor} {ProcFreqMax1}");
            (ac.Class1Mhz, dc.Class1Mhz) = ParseAcDcValues(freqMax1Output);
        }
        catch { /* Class 1 unsupported, ignore */ }

        // Read Class 2 frequency (might not exist on older OS/CPUs)
        try
        {
            var freqMax2Output = RunPowerCfg($"/query SCHEME_CURRENT {SubProcessor} {ProcFreqMax2}");
            (ac.Class2Mhz, dc.Class2Mhz) = ParseAcDcValues(freqMax2Output);
        }
        catch { /* Class 2 unsupported, ignore */ }

        // Read boost mode
        var boostOutput = RunPowerCfg($"/query SCHEME_CURRENT {SubProcessor} {PerfBoostMode}");
        var (acBoost, dcBoost) = ParseAcDcValues(boostOutput);
        ac.BoostMode = (BoostMode)acBoost;
        dc.BoostMode = (BoostMode)dcBoost;

        return (ac, dc);
    }

    /// <summary>
    /// Applies the given AC and DC profiles to the current power scheme.
    /// </summary>
    public void ApplySettings(PowerProfile ac, PowerProfile dc)
    {
        // Class 0 frequency
        RunPowerCfg($"/setACvalueindex SCHEME_CURRENT {SubProcessor} {ProcFreqMax} {ac.Class0Mhz}");
        RunPowerCfg($"/setDCvalueindex SCHEME_CURRENT {SubProcessor} {ProcFreqMax} {dc.Class0Mhz}");

        // Class 1 frequency (might not exist on AMD/single-class CPUs)
        try
        {
            RunPowerCfg($"/setACvalueindex SCHEME_CURRENT {SubProcessor} {ProcFreqMax1} {ac.Class1Mhz}");
            RunPowerCfg($"/setDCvalueindex SCHEME_CURRENT {SubProcessor} {ProcFreqMax1} {dc.Class1Mhz}");
        }
        catch { /* Class 1 unsupported, ignore */ }

        // Class 2 frequency (might not exist)
        try
        {
            RunPowerCfg($"/setACvalueindex SCHEME_CURRENT {SubProcessor} {ProcFreqMax2} {ac.Class2Mhz}");
            RunPowerCfg($"/setDCvalueindex SCHEME_CURRENT {SubProcessor} {ProcFreqMax2} {dc.Class2Mhz}");
        }
        catch { /* Class 2 unsupported, ignore */ }

        // Boost mode (PERFBOOSTMODE)
        RunPowerCfg($"/setACvalueindex SCHEME_CURRENT {SubProcessor} {PerfBoostMode} {(int)ac.BoostMode}");
        RunPowerCfg($"/setDCvalueindex SCHEME_CURRENT {SubProcessor} {PerfBoostMode} {(int)dc.BoostMode}");

        // Activate the scheme for changes to take effect
        RunPowerCfg("/setactive SCHEME_CURRENT");
    }

    /// <summary>
    /// Unhides frequency and boost mode settings in the Windows Power Options GUI
    /// for the given number of core classes (1, 2, or 3).
    /// </summary>
    public void UnhideSettings(int classCount)
    {
        const string baseKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00";
        
        // Helper to set Attribute=2 (Visible)
        void Unhide(string guid)
        {
            try { Registry.SetValue($@"{baseKey}\{guid}", "Attributes", 2, RegistryValueKind.DWord); } catch {}
        }

        // Helper to set Attribute=1 (Hidden)
        void Hide(string guid)
        {
            try { Registry.SetValue($@"{baseKey}\{guid}", "Attributes", 1, RegistryValueKind.DWord); } catch {}
        }

        // Class 0 (PROCFREQMAX) — always present
        Unhide("75b0ae3f-bce0-45a7-8c89-c9611c25e100");

        // Class 1 (PROCFREQMAX1) — Intel hybrid (2+ classes)
        if (classCount >= 2) Unhide("75b0ae3f-bce0-45a7-8c89-c9611c25e101");
        else Hide("75b0ae3f-bce0-45a7-8c89-c9611c25e101");

        // Class 2 (PROCFREQMAX2) — Intel Core Ultra (3 classes)
        if (classCount >= 3) Unhide("75b0ae3f-bce0-45a7-8c89-c9611c25e102");
        else Hide("75b0ae3f-bce0-45a7-8c89-c9611c25e102");

        // Boost mode — always useful
        Unhide("be337238-0d82-4146-a960-4f3749d470c7");
    }

    private static (int AcValue, int DcValue) ParseAcDcValues(string output)
    {
        int acValue = 0, dcValue = 0;

        var acMatch = AcValueRegex().Match(output);
        if (acMatch.Success)
            acValue = int.Parse(acMatch.Groups[1].Value, NumberStyles.HexNumber);

        var dcMatch = DcValueRegex().Match(output);
        if (dcMatch.Success)
            dcValue = int.Parse(dcMatch.Groups[1].Value, NumberStyles.HexNumber);

        return (acValue, dcValue);
    }

    private static string RunPowerCfg(string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(); } catch { }
            throw new InvalidOperationException($"powercfg {arguments} timed out");
        }

        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"powercfg {arguments} failed: {error}");

        return output;
    }
}
