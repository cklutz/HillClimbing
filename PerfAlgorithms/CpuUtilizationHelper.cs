using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PerfAlgorithms
{
    public static class CpuUtilizationHelper
    {
        private static readonly object s_initLock = new object();
        private static Timer s_timer;
        private static ProcessCpuInformation s_prevCpuInfo;

        private static int s_cpuUtilization;

        /// <summary>
        /// If <see cref="Initialize"/> has not been called, this method always returns 0.
        /// </summary>
        /// <returns></returns>
        public static int GetCpuUtilization()
        {
            // Reads on 32bit integers are atomic.
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

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append($"IdleTime={IdleTime}, ");
                sb.Append($"KernelTime={KernelTime}, ");
                sb.Append($"UserTime={UserTime}, ");
                sb.Append($"NumberOfProcessors={NumberOfProcessors}, ");
                sb.Append($"AffinityMask={Convert.ToString(AffinityMask.ToInt64(), 2)}");
                return sb.ToString();
            }
        }

        public static void Initialize(int dueTimeMilliseconds)
        {
            lock (s_initLock)
            {
                if (s_timer != null)
                {
                    return;
                }

                s_prevCpuInfo = new ProcessCpuInformation();
                s_prevCpuInfo.NumberOfProcessors = Environment.ProcessorCount;
                s_prevCpuInfo.AffinityMask = GetCurrentAffinityMask(s_prevCpuInfo.NumberOfProcessors);
                s_prevCpuInfo.UsageBufferSize = s_prevCpuInfo.NumberOfProcessors * Marshal.SizeOf(typeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
                s_prevCpuInfo.UsageBuffer = Marshal.AllocHGlobal(s_prevCpuInfo.UsageBufferSize);

                s_cpuUtilization = GetCpuBusyTime(s_prevCpuInfo);

                s_timer = new Timer(state =>
                    {
                        var pi = (ProcessCpuInformation)state;
                        // Writes to 32bit integers are atomic.
                        s_cpuUtilization = GetCpuBusyTime(pi);
                    }, s_prevCpuInfo,
                    TimeSpan.FromMilliseconds(dueTimeMilliseconds),
                    TimeSpan.FromMilliseconds(dueTimeMilliseconds));
            }
        }

        private static IntPtr GetCurrentAffinityMask(int processorCount)
        {
            IntPtr affinityMask = NativeMethods.GetCurrentProcessAffinityMask();
            if (affinityMask == IntPtr.Zero)
            {
                long mask = 0, maskpos = 1;
                for (int i = 0; i < processorCount; i++)
                {
                    mask |= maskpos;
                    maskpos <<= 1;
                }
                affinityMask = new IntPtr(mask);
            }
            return affinityMask;
        }

        private static int GetCpuBusyTime(ProcessCpuInformation oldInfo)
        {
            var newInfo = new ProcessCpuInformation();

            if (CpuGroupInfo.CanEnableGCCpuGroups() && CpuGroupInfo.CanEnableThreadUseAllCpuGroups())
            {
                // Process can run on all CPUs in the system, regardless of which CPU groups they
                // may be assigned to. Use a simpliefied version of getting the curren times.

                var newIdleTime = new NativeMethods.FILETIME();
                var newKernelTime = new NativeMethods.FILETIME();
                var newUserTime = new NativeMethods.FILETIME();

                if (!NativeMethods.GetSystemTimes(ref newIdleTime, ref newKernelTime, ref newUserTime))
                    throw new Win32Exception();

                newInfo.IdleTime = newIdleTime.dwHighDateTime << 32 | newIdleTime.dwLowDateTime;
                newInfo.KernelTime = newKernelTime.dwHighDateTime << 32 | newKernelTime.dwLowDateTime;
                newInfo.UserTime = newUserTime.dwHighDateTime << 32 | newUserTime.dwLowDateTime;
            }
            else
            {
                // Process may be restricted to certain CPUs (by affinity or groups).

                NativeMethods.ZeroMemory(oldInfo.UsageBuffer, oldInfo.UsageBufferSize);
                int returnedSize;
                int rc = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessorPerformanceInformation,
                    oldInfo.UsageBuffer, oldInfo.UsageBufferSize, out returnedSize);
                if (rc != 0)
                    throw new Win32Exception($"NtQuerySystemInformation({rc})");

                var iter = oldInfo.UsageBuffer;
                var pmask = oldInfo.AffinityMask.ToInt64();

                while (pmask > 0)
                {
                    if ((pmask & 1) != 0)
                    {
                        var data = new NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION();
                        Marshal.PtrToStructure(iter, data);

                        //should be good: 1CPU 28823 years, 256CPUs 100+years
                        newInfo.IdleTime += data.IdleTime.QuadPart;
                        newInfo.KernelTime += data.KernelTime.QuadPart;
                        newInfo.UserTime += data.UserTime.QuadPart;
                    }

                    pmask >>= 1;
                    iter = iter + Marshal.SizeOf(typeof(NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
                }
            }

            long cpuTotalTime = (newInfo.UserTime - oldInfo.UserTime) + (newInfo.KernelTime - oldInfo.KernelTime);
            long cpuBusyTime = cpuTotalTime - (newInfo.IdleTime - oldInfo.IdleTime);

            // Preserve reading
            oldInfo.UserTime = newInfo.UserTime;
            oldInfo.IdleTime = newInfo.IdleTime;
            oldInfo.KernelTime = newInfo.KernelTime;
            // Refetch affinity, might dynamically change during a process' lifetime.
            // (This is not done, it seems, in the CLR.)
            oldInfo.AffinityMask = GetCurrentAffinityMask(oldInfo.NumberOfProcessors);

            long reading = 0;
            if (cpuTotalTime > 0)
                reading = ((cpuBusyTime * 100) / cpuTotalTime);

            return (int)reading;
        }
    }
}