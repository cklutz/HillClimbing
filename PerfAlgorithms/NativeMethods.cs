using System;
using System.ComponentModel;
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
        internal static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

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
        internal static extern bool GetSystemTimes(ref FILETIME lpIdleTime, ref FILETIME lpKernelTime, ref FILETIME lpUserTime);

        internal const int RelationGroup = 4;

        [DllImport(KernelDll, SetLastError = true)]
        internal static extern bool GetLogicalProcessorInformationEx(
            int RelationshipType,
            IntPtr Buffer,
            ref int ReturnedLength);

        [StructLayout(LayoutKind.Sequential)]
        internal class SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
        {
            public int Relationship;
            public int Size;

            public GROUP_RELATIONSHIP Groups;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GROUP_RELATIONSHIP
        {
            public short MaximumGroupCount;
            public short ActiveGroupCount;
            // Other members not need here.
        }

        internal const int ERROR_INSUFFICIENT_BUFFER = 122;

        internal static int GetCpuGroupCount()
        {
            int len = 0;
            var buffer = IntPtr.Zero;
            if (!GetLogicalProcessorInformationEx(RelationGroup, buffer, ref len) &&
                Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER)
                throw new Win32Exception();

            buffer = Marshal.AllocHGlobal(len);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationGroup, buffer, ref len))
                    throw new Win32Exception();

                int byteOffset = 0;
                IntPtr iter = buffer;
                while (byteOffset < len)
                {
                    var info = new SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX();
                    Marshal.PtrToStructure(iter, info);

                    if (info.Relationship == RelationGroup)
                    {
                        return info.Groups.ActiveGroupCount;
                    }

                    byteOffset += info.Size;
                    iter = buffer + byteOffset;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return 0;
        }
    }
}