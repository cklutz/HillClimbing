using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace PerfAlgorithms
{
    internal class NativeMethods
    {
        private const string KernelDll = "kernel32.dll";
        private const string NtDll = "ntdll.dll";

        internal static IntPtr GetCurrentProcessAffinityMask()
        {
            IntPtr pmask;
            IntPtr smask;
            if (!GetProcessAffinityMask(GetCurrentProcess(), out pmask, out smask))
                throw new Win32Exception();

            long mask = pmask.ToInt64();
            mask &= smask.ToInt64();
            return new IntPtr(mask);
        }

        [DllImport(KernelDll, SetLastError = true, ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport(KernelDll, SetLastError = true, ExactSpelling = true)]
        internal static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask,
            out IntPtr lpSystemAffinityMask);

        [StructLayout(LayoutKind.Sequential)]
        internal class SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public LARGE_INTEGER IdleTime;
            public LARGE_INTEGER KernelTime;
            public LARGE_INTEGER UserTime;
            public LARGE_INTEGER DpcTime;
            public LARGE_INTEGER InterruptTime;
            public int InterruptCount;
        }

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        internal struct LARGE_INTEGER
        {
            [FieldOffset(0)] public Int64 QuadPart;
            [FieldOffset(0)] public UInt32 LowPart;
            [FieldOffset(4)] public Int32 HighPart;
        }


        internal const int SystemProcessorPerformanceInformation = 0x08;

        [DllImport(NtDll, CharSet = CharSet.Auto)]
        internal static extern int NtQuerySystemInformation(int query, IntPtr dataPtr, int size, out int returnedSize);

        [DllImport(KernelDll, EntryPoint = "RtlZeroMemory", SetLastError = false)]
        internal static extern void ZeroMemory(IntPtr dest, int size);


        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport(KernelDll, SetLastError = true)]
        internal static extern bool GetSystemTimes(ref FILETIME lpIdleTime, ref FILETIME lpKernelTime,
            ref FILETIME lpUserTime);

        internal const int RelationProcessorCore = 0;
        internal const int RelationNumaNode = 1;
        internal const int RelationCache = 2;
        internal const int RelationProcessorPackage = 3;
        internal const int RelationGroup = 4;

        [DllImport(KernelDll, SetLastError = true)]
        internal static extern bool GetLogicalProcessorInformationEx(
            int RelationshipType,
            IntPtr Buffer,
            ref int ReturnedLength);

        internal interface ISystemLogicalProcessoInformation
        {
            int _RelationShip { get; }
            int _Size { get;  }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_GROUP : ISystemLogicalProcessoInformation
        {
            public int Relationship;
            public int Size;
            public GROUP_RELATIONSHIP Groups;

            public int _RelationShip => Relationship;
            public int _Size => Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_CACHE : ISystemLogicalProcessoInformation
        {
            public int Relationship;
            public int Size;
            public CACHE_RELATIONSHIP Cache;

            public int _RelationShip => Relationship;
            public int _Size => Size;
        }

        internal enum PROCESSOR_CACHE_TYPE
        {
            CacheUnified,
            CacheInstruction,
            CacheData,
            CacheTrace
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CACHE_RELATIONSHIP
        {
            public byte Level;
            public byte Associativity;
            public short LineSize;
            public int CacheSize;
            public PROCESSOR_CACHE_TYPE Type;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)] public byte[] Reserved;
            public GROUP_AFFINITY GroupMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GROUP_AFFINITY
        {
            public IntPtr Mask;
            public int Group;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GROUP_RELATIONSHIP
        {
            public short MaximumGroupCount;
            public short ActiveGroupCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)] public byte[] Reserved;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public PROCESSOR_GROUP_INFO[] GroupInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESSOR_GROUP_INFO
        {
            public byte MaximumProcessorCount;
            public byte ActiveProcessorCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 38)] public byte[] Reserved;
            public IntPtr ActiveProcessorMask;
        }

        internal const int ERROR_INSUFFICIENT_BUFFER = 122;

        internal static void GetLogicalProcessorInformationEx<T>(int relation, Func<T, bool> worker)
            where T : ISystemLogicalProcessoInformation
        {
            int len = 0;
            var buffer = IntPtr.Zero;
            if (!GetLogicalProcessorInformationEx(relation, buffer, ref len) &&
                Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                throw new Win32Exception();

            buffer = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetLogicalProcessorInformationEx(relation, buffer, ref len))
                    throw new Win32Exception();

                int byteOffset = 0;
                IntPtr iter = buffer;
                while (byteOffset < len)
                {
                    var info = Marshal.PtrToStructure<T>(iter);
                    if (info._RelationShip == relation)
                    {
                        if (!worker(info))
                        {
                            break;
                        }
                    }

                    byteOffset += info._Size;
                    iter = buffer + byteOffset;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Returns the number of processors that a process has been configured to run on.
        /// Note this is not equal to <see cref="Environment.ProcessorCount"/>.
        /// </summary>
        /// <returns></returns>
        internal static int GetCurrentProcessCpuCount()
        {
            IntPtr pmaskPtr, smaskPtr;
            if (!GetProcessAffinityMask(GetCurrentProcess(), out pmaskPtr, out smaskPtr))
                throw new Win32Exception();

            long pmask = pmaskPtr.ToInt64();
            long smask = smaskPtr.ToInt64();

            if (pmask == 1)
                return 1;

            pmask &= smask;

            int count = 0;
            while (pmask > 0)
            {
                if ((pmask & 1) != 0)
                    count++;

                pmask >>= 1;
            }

            // GetProcessAffinityMask can return pmask=0 and smask=0 on systems with more
            // than 64 processors, which would leave us with a count of 0.  Since the GC
            // expects there to be at least one processor to run on (and thus at least one
            // heap), we'll return 64 here if count is 0, since there are likely a ton of
            // processors available in that case.  The GC also cannot (currently) handle
            // the case where there are more than 64 processors, so we will return a
            // maximum of 64 here.
            if (count == 0 || count > 64)
                count = 64;

            return count;
        }
    }
}