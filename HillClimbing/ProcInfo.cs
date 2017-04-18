using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HillClimbing
{
    public static class ProcInfo
    {
        // -------------------------------------------------------------

        private static void Test()
        {
            ProcInfo.Initialize();

            while (true)
            {
               ConsumeCPU(50); 
            }
        }

        private static void ConsumeCPU(int percentage)
        {
            if (percentage < 0 || percentage > 100)
                throw new ArgumentException("percentage");
            Stopwatch watch = new Stopwatch();
            watch.Start();            
            while (true)
            {
                // Make the loop go on for "percentage" milliseconds then sleep the 
                // remaining percentage milliseconds. So 40% utilization means work 40ms and sleep 60ms
                if (watch.ElapsedMilliseconds > percentage)
                {
                    Thread.Sleep(100 - percentage);
                    watch.Reset();
                    watch.Start();
                }
            }
        }

        // -------------------------------------------------------------


        private static readonly object s_initLock = new object();
        private static Timer s_timer;

        private static int s_cpuUtilization;

        public static int GetCpuUtilization()
        {
            return s_cpuUtilization;
        }


        private class ProcessCpuInformation
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public IntPtr AffinityMask;
            public int NumberOfProcessors;
            public IntPtr UsageBuffer;
            public int UsageBufferSize;
        }

        public static void Initialize()
        {
            lock (s_initLock)
            {
                if (s_timer != null)
                {
                    return;
                }

                var prevCpuInfo = new ProcessCpuInformation();
                prevCpuInfo.NumberOfProcessors = Environment.ProcessorCount;

                // In following cases, affinity mask can be zero
                // 1. hosted, the hosted process already uses multiple cpu groups.
                //    thus, during CLR initialization, GetCurrentProcessCpuCount() returns 64, and GC threads
                //    are created to fill up the initial CPU group. ==> use g_SystemInfo.dwNumberOfProcessors
                // 2. GCCpuGroups=1, CLR creates GC threads for all processors in all CPU groups
                //    thus, the threadpool thread would use a whole CPU group (if Thread_UseAllCpuGroups is not set).
                //    ==> use g_SystemInfo.dwNumberOfProcessors.
                // 3. !defined(FEATURE_PAL) but defined(FEATURE_CORESYSTEM), GetCurrentProcessCpuCount()
                //    returns g_SystemInfo.dwNumberOfProcessors ==> use g_SystemInfo.dwNumberOfProcessors;
                // Other cases:
                // 1. Normal case: the mask is all or a subset of all processors in a CPU group;
                // 2. GCCpuGroups=1 && Thread_UseAllCpuGroups = 1, the mask is not used
                // 
                prevCpuInfo.AffinityMask = NativeMethods.GetCurrentProcessAffinityMask();
                if (prevCpuInfo.AffinityMask == IntPtr.Zero)
                {
                    long mask = 0, maskpos = 1;
                    for (int i = 0; i < prevCpuInfo.NumberOfProcessors; i++)
                    {
                        mask |= maskpos;
                        maskpos <<= 1;
                    }
                    prevCpuInfo.AffinityMask = new IntPtr(mask);
                }

                prevCpuInfo.UsageBufferSize = prevCpuInfo.NumberOfProcessors * Marshal.SizeOf(typeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
                prevCpuInfo.UsageBuffer = Marshal.AllocHGlobal(prevCpuInfo.UsageBufferSize);

                s_cpuUtilization = GetCpuBusyTime(prevCpuInfo);

                s_timer = new Timer(state =>
                {
                    var pi = (ProcessCpuInformation)state;
                    s_cpuUtilization = GetCpuBusyTime(pi);
                    Console.WriteLine(s_cpuUtilization);
                }, prevCpuInfo,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500));
            }
        }

        private static int GetCpuBusyTime(ProcessCpuInformation oldInfo)
        {
            NativeMethods.ZeroMemory(oldInfo.UsageBuffer, oldInfo.UsageBufferSize);

            int returnedSize;
            int rc = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessorPerformanceInformation,
                oldInfo.UsageBuffer, oldInfo.UsageBufferSize, out returnedSize);
            if (rc != 0)
            {
                throw new Win32Exception($"NtQuerySystemInformation({rc})");
            }

            var newInfo = new ProcessCpuInformation();

            var iter = oldInfo.UsageBuffer;
            var pmask = oldInfo.AffinityMask.ToInt64();
            int procNo = 0;
            while (pmask > 0)
            {
                var data = new NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION();
                Marshal.PtrToStructure(iter, data);
                iter = iter + Marshal.SizeOf(typeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));

                if ((pmask & 1) != 0)
                {
                    //should be good: 1CPU 28823 years, 256CPUs 100+years
                    newInfo.IdleTime += data.IdleTime;
                    newInfo.KernelTime += data.KernelTime;
                    newInfo.UserTime += data.UserTime;
                }

                pmask >>= 1;
                procNo++;
            }

            long cpuTotalTime = (newInfo.UserTime - oldInfo.UserTime) + (newInfo.KernelTime - oldInfo.KernelTime);
            long cpuBusyTime = cpuTotalTime - (newInfo.IdleTime - oldInfo.IdleTime);

            // Preserve reading
            oldInfo.UserTime = newInfo.UserTime;
            oldInfo.IdleTime = newInfo.IdleTime;
            oldInfo.KernelTime = newInfo.KernelTime;

            long reading = 0;
            if (cpuTotalTime > 0)
                reading = ((cpuBusyTime * 100) / cpuTotalTime);

            return (int)reading;
        }
    }

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
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public int InterruptCount;
        }


        internal const int SystemProcessorPerformanceInformation = 0x08;

        [DllImport(NtDll, CharSet = CharSet.Auto)]
        internal static extern int NtQuerySystemInformation(int query, IntPtr dataPtr, int size, out int returnedSize);

        [DllImport(KernelDll, EntryPoint = "RtlZeroMemory", SetLastError = false)]
        internal static extern void ZeroMemory(IntPtr dest, int size);
    }
}
