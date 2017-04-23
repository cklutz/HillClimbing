using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PerfAlgorithms
{
    public class CpuCoreInfo
    {
        public static void Dump(TextWriter tw = null)
        {
            tw = tw ?? Console.Out;

            // Note: for RelationshipProcessorCore, the "GroupCount" is always 1.
            // That is, we only have one GroupMask to deal with.

            int maxMask = s_data.Value.Select(x => x.GroupMask[0].Mask.ToInt64())
                .Max(n => Convert.ToString(n, 2).Length);

            for (var i = 0; i < s_data.Value.Count; i++)
            {
                var entry = s_data.Value[i];
                string map = new string(Convert.ToString(entry.GroupMask[0].Mask.ToInt64(), 2).Reverse().ToArray());
                tw.Write(map.Replace('1', '*').Replace('0', '-').PadRight(maxMask, '-'));
                tw.Write(" ");
                tw.Write("Core ");
                tw.Write(i);
                tw.Write(" ");
                tw.Write(entry.Flags == NativeMethods.LPT_PC_SMT ? "(Hyperthreaded)" : "");
                tw.WriteLine();
            }
        }

        public int NumberOfCores => s_data.Value.Count;

        public bool IsHyperThreaded(int cpuCoreIndex)
        {
            return s_data.Value[cpuCoreIndex].Flags == NativeMethods.LPT_PC_SMT;
        }

        private static readonly Lazy<List<NativeMethods.PROCESSOR_RELATIONSHIP>> s_data =
            new Lazy<List<NativeMethods.PROCESSOR_RELATIONSHIP>>(() => GetNativeInfo().ToList());


        private static IEnumerable<NativeMethods.PROCESSOR_RELATIONSHIP> GetNativeInfo()
        {
            var result = new List<NativeMethods.PROCESSOR_RELATIONSHIP>();
            NativeMethods
                .GetLogicalProcessorInformationEx<NativeMethods.SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_PROCESSOR>(
                    NativeMethods.RelationProcessorCore,
                    info =>
                    {
                        result.Add(info.Processor);
                        return true;
                    });

            return result;
        }
    }
}