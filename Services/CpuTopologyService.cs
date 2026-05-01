using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        public IntPtr GroupMask;
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
        // First call gets the required buffer size
        GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref len);
        
        if (len == 0) return 1; // Fallback
        
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
            // Ignore any P/Invoke errors and fallback to 1 class
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return 1; // Default fallback
    }
}
