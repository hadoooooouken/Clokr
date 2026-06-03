using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Clokr.Models;

namespace Clokr.Services;

public class CpuTopologyService
{
    public enum LOGICAL_PROCESSOR_RELATIONSHIP
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xffff
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
    {
        public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
        public uint Size;
        public PROCESSOR_RELATIONSHIP Processor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSOR_RELATIONSHIP
    {
        public byte Flags;
        public byte EfficiencyClass;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Reserved;
        public ushort GroupCount;
        // This is followed by an array of GROUP_AFFINITY structures
        public GROUP_AFFINITY GroupMask; 
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GROUP_AFFINITY
    {
        public UIntPtr Mask;
        public ushort Group;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CACHE_RELATIONSHIP
    {
        public byte Level;
        public byte Associativity;
        public ushort LineSize;
        public uint CacheSize;
        public uint Type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Reserved;
        public ushort GroupCount;
        public GROUP_AFFINITY GroupMask;
    }

    private struct LogicalProcessorInfo
    {
        public byte EfficiencyClass;
        public int CoreId;
    }

    /// <summary>
    /// Combines OS scheduling class with CPUID hardware info to uniquely identify core classes.
    /// Sorts descending: P-cores first, then regular E-cores, then LP E-cores.
    /// </summary>
    private record struct CompositeCoreKey(byte EfficiencyClass, byte CoreType, int NativeModelId) : IComparable<CompositeCoreKey>
    {
        public int CompareTo(CompositeCoreKey other)
        {
            // 1. Descending CoreType (0x40 = P-core, 0x20 = E-core)
            int c = other.CoreType.CompareTo(CoreType);
            if (c != 0) return c;

            // 2. Descending EfficiencyClass (Windows scheduling priority)
            c = other.EfficiencyClass.CompareTo(EfficiencyClass);
            if (c != 0) return c;

            // 3. Descending NativeModelId (higher represents newer/stronger architectures, e.g. Skymont 3 > Crestmont 2)
            return other.NativeModelId.CompareTo(NativeModelId);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType,
        IntPtr Buffer,
        ref uint ReturnedLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    /// <summary>
    /// Returns unique physical core IDs and their scheduler efficiency classes.
    /// </summary>
    private Dictionary<int, LogicalProcessorInfo> GetLogicalProcessorTopology()
    {
        var result = new Dictionary<int, LogicalProcessorInfo>();
        uint len = 0;
        GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref len);
        if (len == 0) return result;

        IntPtr ptr = Marshal.AllocHGlobal((int)len);
        try
        {
            if (GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, ptr, ref len))
            {
                IntPtr currentPtr = ptr;
                long endPtr = ptr.ToInt64() + len;
                int coreId = 0;

                while (currentPtr.ToInt64() < endPtr)
                {
                    var info = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(currentPtr);
                    if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                    {
                        byte effClass = info.Processor.EfficiencyClass;
                        IntPtr maskPtr = new IntPtr(currentPtr.ToInt64() + Marshal.OffsetOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>("Processor").ToInt64() + Marshal.OffsetOf<PROCESSOR_RELATIONSHIP>("GroupMask").ToInt64());
                        
                        for (int i = 0; i < info.Processor.GroupCount; i++)
                        {
                            var affinity = Marshal.PtrToStructure<GROUP_AFFINITY>(new IntPtr(maskPtr.ToInt64() + i * Marshal.SizeOf<GROUP_AFFINITY>()));
                            ulong mask = (ulong)affinity.Mask;
                            for (int bit = 0; bit < 64; bit++)
                            {
                                if (((mask >> bit) & 1) == 1)
                                {
                                    int logicalIndex = affinity.Group * 64 + bit;
                                    result[logicalIndex] = new LogicalProcessorInfo 
                                    { 
                                        EfficiencyClass = effClass, 
                                        CoreId = coreId 
                                    };
                                }
                            }
                        }
                        coreId++;
                    }
                    currentPtr = new IntPtr(currentPtr.ToInt64() + info.Size);
                }
            }
        }
        catch {}
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return result;
    }

    /// <summary>
    /// Groups logical processors into distinct core classes using OS topology + CPUID 0x1A + CPUID 0x1F.
    /// On CPUs where CPUID 0x1A and Windows EfficiencyClass cannot distinguish LP E-cores from regular
    /// E-cores (e.g. Arrow Lake-U), uses CPUID 0x1F module/tile topology to detect the split.
    /// </summary>
    private Dictionary<CompositeCoreKey, List<int>> DetectCoreGroups(Dictionary<int, LogicalProcessorInfo> topology)
    {
        var groups = new Dictionary<CompositeCoreKey, List<int>>();
        int logicalProcessorCount = Environment.ProcessorCount;

        bool hasCpuIdSupport = X86Base.IsSupported;
        bool isHybrid = false;
        int maxLeaf = 0;

        if (hasCpuIdSupport)
        {
            try
            {
                var leaf0 = X86Base.CpuId(0, 0);
                maxLeaf = leaf0.Eax;
                var leaf7 = X86Base.CpuId(7, 0);
                isHybrid = ((leaf7.Edx >> 15) & 1) == 1;
            }
            catch { hasCpuIdSupport = false; }
        }

        bool hasLeaf1F = hasCpuIdSupport && maxLeaf >= 0x1F;

        IntPtr hThread = GetCurrentThread();

        // Per-LP module group ID from CPUID 0x1F, used for LP E-core splitting
        var lpModuleIds = new Dictionary<int, int>();

        for (int i = 0; i < logicalProcessorCount; i++)
        {
            byte coreType = 0;
            int nativeModelId = 0;
            byte effClass = 0;
            int moduleGroupId = 0;

            if (topology.TryGetValue(i, out var topInfo))
            {
                effClass = topInfo.EfficiencyClass;
            }

            if (hasCpuIdSupport && isHybrid)
            {
                UIntPtr mask = (UIntPtr)(1UL << i);
                UIntPtr prevMask = SetThreadAffinityMask(hThread, mask);
                if (prevMask != UIntPtr.Zero)
                {
                    try
                    {
                        Thread.Sleep(1);

                        // CPUID 0x1A: Core type and native model identification
                        var result1A = X86Base.CpuId(0x1A, 0);
                        coreType = (byte)((uint)result1A.Eax >> 24);
                        nativeModelId = result1A.Eax & 0x00FFFFFF;

                        // CPUID 0x1F: V2 Extended Topology Enumeration
                        // Enumerates topology levels (SMT→Core→Module→Tile→Die).
                        // The Core-level shift tells us how many x2APIC ID bits encode
                        // sub-core (SMT) + core-within-module. Shifting right by this
                        // value gives the module/tile group ID.
                        if (hasLeaf1F)
                        {
                            int coreShift = 0;
                            int x2apicId = 0;
                            bool foundCoreLevel = false;

                            for (int subleaf = 0; subleaf < 16; subleaf++)
                            {
                                var r = X86Base.CpuId(0x1F, subleaf);
                                int levelType = (r.Ecx >> 8) & 0xFF;
                                x2apicId = r.Edx;

                                if (levelType == 0) break;

                                if (levelType == 2) // Core level
                                {
                                    coreShift = r.Eax & 0x1F;
                                    foundCoreLevel = true;
                                }
                            }

                            if (foundCoreLevel && coreShift > 0)
                            {
                                moduleGroupId = x2apicId >> coreShift;
                            }
                        }
                    }
                    catch {}
                    finally
                    {
                        SetThreadAffinityMask(hThread, prevMask);
                    }
                }
            }

            lpModuleIds[i] = moduleGroupId;

            var key = new CompositeCoreKey(effClass, coreType, nativeModelId);
            if (!groups.ContainsKey(key))
                groups[key] = new List<int>();
            groups[key].Add(i);
        }

        // Post-processing: detect LP E-cores by tile boundary analysis.
        // On Arrow Lake-U, Windows EfficiencyClass and CPUID 0x1A return identical values
        // for Skymont (regular E) and Crestmont (LP E) cores. But they reside on different
        // physical tiles: regular E-cores on the compute tile alongside P-cores, LP E-cores
        // on the separate SoC tile. CPUID 0x1F module IDs reflect this: compute tile modules
        // have contiguous IDs (e.g. 0,1,2,3) while SoC tile modules are isolated (e.g. 8).
        //
        // Algorithm: flood-fill from P-core module IDs, expanding to adjacent (±1) E-core
        // modules to determine the compute tile boundary. E-core modules NOT reachable by
        // this expansion are on the SoC tile (LP E-cores).
        if (isHybrid && hasLeaf1F)
        {
            // Collect P-core module IDs as compute tile anchor points
            var pCoreModuleIds = new HashSet<int>();
            foreach (var kvp in groups)
            {
                if (kvp.Key.CoreType == 0x40) // Intel Core (P-core)
                {
                    foreach (var lp in kvp.Value)
                        pCoreModuleIds.Add(lpModuleIds[lp]);
                }
            }

            if (pCoreModuleIds.Count > 0)
            {
                var eCoreKeys = groups.Keys.Where(k => k.CoreType == 0x20).ToList();

                foreach (var originalKey in eCoreKeys)
                {
                    var lps = groups[originalKey];
                    var eCoreModuleIds = lps.Select(lp => lpModuleIds[lp]).Distinct().ToHashSet();

                    // Flood-fill: start with P-core modules, expand to include any E-core
                    // module whose ID is within ±1 of an already-included module
                    var computeTileModules = new HashSet<int>(pCoreModuleIds);
                    bool expanded = true;
                    while (expanded)
                    {
                        expanded = false;
                        foreach (var emod in eCoreModuleIds)
                        {
                            if (computeTileModules.Contains(emod)) continue;
                            if (computeTileModules.Any(ct => Math.Abs(emod - ct) <= 1))
                            {
                                computeTileModules.Add(emod);
                                expanded = true;
                            }
                        }
                    }

                    // Split: compute tile = regular E-cores, everything else = LP E-cores
                    var computeTileLPs = lps.Where(lp => computeTileModules.Contains(lpModuleIds[lp])).ToList();
                    var socTileLPs = lps.Where(lp => !computeTileModules.Contains(lpModuleIds[lp])).ToList();

                    if (socTileLPs.Count > 0 && computeTileLPs.Count > 0)
                    {
                        // Regular E-cores keep the original key
                        groups[originalKey] = computeTileLPs;

                        // LP E-cores get a new key with decremented NativeModelId (sorts after regular E in descending order)
                        var lpKey = new CompositeCoreKey(originalKey.EfficiencyClass, originalKey.CoreType, originalKey.NativeModelId - 1);
                        groups[lpKey] = socTileLPs;
                    }
                }
            }
        }

        return groups;
    }

    /// <summary>
    /// Detects the number of unique CPU core efficiency classes.
    /// Returns 1 for standard CPUs, 2 for hybrid (e.g. Alder/Raptor Lake), 3 for 3-tier hybrid (e.g. Arrow Lake / Meteor Lake).
    /// </summary>
    public int GetCoreClassCount()
    {
        try
        {
            var topology = GetLogicalProcessorTopology();
            var groups = DetectCoreGroups(topology);
            return Math.Max(1, groups.Count);
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Counts unique physical cores in a group of logical processor indices.
    /// </summary>
    private int CountPhysicalCores(List<int> logicalProcessors, Dictionary<int, LogicalProcessorInfo> topology)
    {
        return logicalProcessors
            .Select(lp => topology.TryGetValue(lp, out var t) ? t.CoreId : -1)
            .Where(id => id != -1)
            .Distinct()
            .Count();
    }

    public CpuInfo GetCpuDetails()
    {
        var info = new CpuInfo();

        var topology = GetLogicalProcessorTopology();
        var groups = DetectCoreGroups(topology);

        info.LogicalProcessors = Environment.ProcessorCount;
        info.PhysicalCores = topology.Values.Select(t => t.CoreId).Distinct().Count();
        if (info.PhysicalCores == 0)
        {
            info.PhysicalCores = info.LogicalProcessors; // Fallback
        }

        var sortedKeys = groups.Keys.ToList();
        sortedKeys.Sort(); // Sort descending (P -> E -> LPE)

        info.CoreClassCount = Math.Max(1, sortedKeys.Count);

        if (info.CoreClassCount == 3)
        {
            info.P_Cores = CountPhysicalCores(groups[sortedKeys[0]], topology);
            info.E_Cores = CountPhysicalCores(groups[sortedKeys[1]], topology);
            info.LPE_Cores = CountPhysicalCores(groups[sortedKeys[2]], topology);
        }
        else if (info.CoreClassCount == 2)
        {
            info.P_Cores = CountPhysicalCores(groups[sortedKeys[0]], topology);
            info.E_Cores = CountPhysicalCores(groups[sortedKeys[1]], topology);
        }
        else
        {
            info.P_Cores = info.PhysicalCores;
        }

        uint len = 0;
        // Use RelationAll to get cache information in one buffer
        GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, IntPtr.Zero, ref len);

        if (len > 0)
        {
            IntPtr ptr = Marshal.AllocHGlobal((int)len);
            try
            {
                if (GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, ptr, ref len))
                {
                    IntPtr currentPtr = ptr;
                    long endPtr = ptr.ToInt64() + len;
                    long l2Total = 0;
                    long l3Total = 0;

                    while (currentPtr.ToInt64() < endPtr)
                    {
                        var structInfo = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(currentPtr);
                        if (structInfo.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache)
                        {
                            var cachePtr = new IntPtr(currentPtr.ToInt64() + Marshal.OffsetOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>("Processor").ToInt64());
                            var cache = Marshal.PtrToStructure<CACHE_RELATIONSHIP>(cachePtr);
                            if (cache.Level == 2) l2Total += cache.CacheSize;
                            else if (cache.Level == 3) l3Total += cache.CacheSize;
                        }
                        currentPtr = new IntPtr(currentPtr.ToInt64() + structInfo.Size);
                    }

                    info.L2CacheMB = (int)(l2Total / (1024 * 1024));
                    info.L3CacheMB = (int)(l3Total / (1024 * 1024));
                }
            }
            catch {}
            finally { Marshal.FreeHGlobal(ptr); }
        }

        // Get Base Frequency
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var mhz = Convert.ToDouble(obj["MaxClockSpeed"]);
                info.BaseFrequencyGHz = Math.Round(mhz / 1000.0, 2);
                break;
            }
        }
        catch { }

        // Get BIOS Info
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (var obj in searcher.Get())
            {
                var version = obj["SMBIOSBIOSVersion"]?.ToString() ?? "Unknown";
                var rawDate = obj["ReleaseDate"]?.ToString(); // yyyymmdd...
                if (rawDate != null && rawDate.Length >= 8)
                {
                    var date = $"{rawDate.Substring(6, 2)}.{rawDate.Substring(4, 2)}.{rawDate.Substring(0, 4)}";
                    info.BiosInfo = $"{version} ({date})";
                }
                else
                {
                    info.BiosInfo = version;
                }
                break;
            }
        }
        catch { }

        // Get Motherboard Info
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                var mfr = obj["Manufacturer"]?.ToString()?.Trim() ?? "Unknown";
                var prod = obj["Product"]?.ToString()?.Trim() ?? "Unknown";
                info.Motherboard = $"{mfr} {prod}";
                break;
            }
        }
        catch { }

        // Get RAM Info (Sum of all sticks to get "Installed" RAM)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
            long totalCapacity = 0;
            foreach (var obj in searcher.Get())
            {
                totalCapacity += Convert.ToInt64(obj["Capacity"]);
            }
            
            var gb = Math.Round(totalCapacity / (1024.0 * 1024.0 * 1024.0), 1);
            info.RamInfo = $"{gb:0.0} GB";
        }
        catch { }

        return info;
    }
}
