using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PerfAlgorithms
{
    public class CpuNumaNodeInfo
    {
        public static void Dump(TextWriter tw = null)
        {
            tw = tw ?? Console.Out;

            int maxMask = s_data.Value.Select(x => x.GroupMask.Mask.ToInt64()).Max(n => Convert.ToString(n, 2).Length);

            for (var i = 0; i < s_data.Value.Count; i++)
            {
                var entry = s_data.Value[i];
                string map = new string(Convert.ToString(entry.GroupMask.Mask.ToInt64(), 2).Reverse().ToArray());
                tw.Write(map.Replace('1', '*').Replace('0', '-').PadRight(maxMask, '-'));
                tw.Write(" ");
                tw.Write("NUMA Node ");
                tw.Write(i);
                tw.WriteLine();
            }
        }

        public static int NumberOfNumaNodes => s_data.Value.Count;

        private static readonly Lazy<List<NativeMethods.NUMA_NODE_RELATIONSHIP>> s_data =
            new Lazy<List<NativeMethods.NUMA_NODE_RELATIONSHIP>>(() => GetNativeInfo().ToList());

        private static IEnumerable<NativeMethods.NUMA_NODE_RELATIONSHIP> GetNativeInfo()
        {
            var result = new List<NativeMethods.NUMA_NODE_RELATIONSHIP>();
            NativeMethods
                .GetLogicalProcessorInformationEx<NativeMethods.SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_NUMA_NODE>(
                    NativeMethods.RelationNumaNode,
                    info =>
                    {
                        result.Add(info.NumaNode);
                        return true;
                    });

            return result;
        }
    }
}