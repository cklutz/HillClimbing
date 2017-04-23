using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PerfAlgorithms
{
    public class CpuSocketInfo
    {
        public static void Dump(TextWriter tw = null)
        {
            tw = tw ?? Console.Out;

            int maxMask = s_data.Value.Max(p => p.GroupMask.Max(m => Convert.ToString(m.Mask.ToInt64(), 2).Length));

            for (var i = 0; i < s_data.Value.Count; i++)
            {
                var entry = s_data.Value[i];
                // TODO: Wrong when we have multiple groups.
                string map = new string(Convert.ToString(entry.GroupMask[0].Mask.ToInt64(), 2).Reverse().ToArray());
                tw.Write(map.Replace('1', '*').Replace('0', '-').PadRight(maxMask, '-'));
                tw.Write(" ");
                tw.Write("Socket ");
                tw.Write(i);
                tw.WriteLine();
            }
        }

        public static int NumberOfSockets => s_data.Value.Count;

        private static readonly Lazy<List<NativeMethods.PROCESSOR_RELATIONSHIP>> s_data =
            new Lazy<List<NativeMethods.PROCESSOR_RELATIONSHIP>>(() => GetNativeInfo().ToList());


        private static IEnumerable<NativeMethods.PROCESSOR_RELATIONSHIP> GetNativeInfo()
        {
            var result = new List<NativeMethods.PROCESSOR_RELATIONSHIP>();
            NativeMethods
                .GetLogicalProcessorInformationEx<NativeMethods.SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_PROCESSOR>(
                    NativeMethods.RelationProcessorPackage,
                    info =>
                    {
                        result.Add(info.Processor);
                        return true;
                    });

            return result;
        }
    }
}