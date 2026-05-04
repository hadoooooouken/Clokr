using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType,
        IntPtr Buffer,
        ref uint ReturnedLength);

    /// <summary>
    /// Detects the number of unique CPU core efficiency classes.
    /// Returns 1 for standard CPUs, 2 for hybrid (e.g. Alder/Raptor Lake), 3 for 3-tier hybrid (e.g. Meteor Lake).
    /// </summary>
    public int GetCoreClassCount()
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref len);
        
        if (len == 0) return 1;
        
        IntPtr ptr = Marshal.AllocHGlobal((int)len);
        try
        {
            if (GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, ptr, ref len))
            {
                IntPtr currentPtr = ptr;
                long endPtr = ptr.ToInt64() + len;
                var classes = new HashSet<byte>();
                
                while (currentPtr.ToInt64() < endPtr)
                {
                    var info = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(currentPtr);
                    if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                    {
                        classes.Add(info.Processor.EfficiencyClass);
                    }
                    currentPtr = new IntPtr(currentPtr.ToInt64() + info.Size);
                }
                
                return Math.Max(1, classes.Count);
            }
        }
        catch
        {
            // Fallback
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return 1;
    }

    public CpuInfo GetCpuDetails()
    {
        var info = new CpuInfo();
        uint len = 0;
        // Use RelationAll to get cores, cache, and everything else in one buffer
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
                    var classCounts = new Dictionary<byte, (int Physical, int Logical)>();
                    
                    long l2Total = 0;
                    long l3Total = 0;

                    while (currentPtr.ToInt64() < endPtr)
                    {
                        var structInfo = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(currentPtr);
                        
                        if (structInfo.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                        {
                            byte effClass = structInfo.Processor.EfficiencyClass;
                            if (!classCounts.ContainsKey(effClass))
                                classCounts[effClass] = (0, 0);

                            int logicalCount = 0;
                            IntPtr maskPtr = new IntPtr(currentPtr.ToInt64() + Marshal.OffsetOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>("Processor").ToInt64() + Marshal.OffsetOf<PROCESSOR_RELATIONSHIP>("GroupMask").ToInt64());
                            
                            for (int i = 0; i < structInfo.Processor.GroupCount; i++)
                            {
                                var affinity = Marshal.PtrToStructure<GROUP_AFFINITY>(new IntPtr(maskPtr.ToInt64() + i * Marshal.SizeOf<GROUP_AFFINITY>()));
                                logicalCount += CountSetBits(affinity.Mask);
                            }

                            var current = classCounts[effClass];
                            classCounts[effClass] = (current.Physical + 1, current.Logical + logicalCount);
                            
                            info.PhysicalCores++;
                            info.LogicalProcessors += logicalCount;
                        }
                        else if (structInfo.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache)
                        {
                            // Map Cache relationship
                            // Since the union starts at the same offset as Processor
                            var cachePtr = new IntPtr(currentPtr.ToInt64() + Marshal.OffsetOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>("Processor").ToInt64());
                            var cache = Marshal.PtrToStructure<CACHE_RELATIONSHIP>(cachePtr);
                            
                            // Windows reports cache PER INSTANCE (per core, per cluster, or per CCD).
                            // Sum all instances to get totals (important for AMD multi-CCD with separate L3 per CCD).
                            if (cache.Level == 2) l2Total += cache.CacheSize;
                            else if (cache.Level == 3) l3Total += cache.CacheSize;
                        }
                        currentPtr = new IntPtr(currentPtr.ToInt64() + structInfo.Size);
                    }

                    info.L2CacheMB = (int)(l2Total / (1024 * 1024));
                    info.L3CacheMB = (int)(l3Total / (1024 * 1024));

                    // Map classes to P/E/LPE
                    int classCount = classCounts.Count;
                    info.CoreClassCount = Math.Max(1, classCount);
                    var sortedClasses = new List<byte>(classCounts.Keys);
                    sortedClasses.Sort(); // Usually 0, 1, 2

                    if (classCount == 3)
                    {
                        // 0: LPE, 1: E, 2: P
                        info.LPE_Cores = classCounts[sortedClasses[0]].Physical;
                        info.E_Cores = classCounts[sortedClasses[1]].Physical;
                        info.P_Cores = classCounts[sortedClasses[2]].Physical;
                    }
                    else if (classCount == 2)
                    {
                        // 0: E, 1: P
                        info.E_Cores = classCounts[sortedClasses[0]].Physical;
                        info.P_Cores = classCounts[sortedClasses[1]].Physical;
                    }
                    else if (classCount == 1)
                    {
                        info.P_Cores = classCounts[sortedClasses[0]].Physical;
                    }
                }
            }
            catch { }
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

    private int CountSetBits(UIntPtr mask)
    {
        ulong v = (ulong)mask;
        int count = 0;
        while (v > 0)
        {
            v &= (v - 1);
            count++;
        }
        return count;
    }
}
