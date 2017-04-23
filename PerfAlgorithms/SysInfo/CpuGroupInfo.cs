using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PerfAlgorithms
{
    public class CpuGroupInfo
    {
        public static int NumberOfProcessors => s_data.Value.Sum(g => g.ActiveProcessorCount);
        public static int GroupCount => s_data.Value.Count;

        public static void Dump(TextWriter tw = null)
        {
            tw = tw ?? Console.Out;

            int maxMask = s_data.Value.Max(m => Convert.ToString(m.ActiveProcessorMask.ToInt64(), 2).Length);

            for (int i = 0; i < s_data.Value.Count; i++)
            {
                var entry = s_data.Value[i];
                string map = new string(Convert.ToString(entry.ActiveProcessorMask.ToInt64(), 2).Reverse().ToArray());
                tw.Write(map.Replace('1', '*').Replace('0', '-').PadRight(maxMask, '-'));
                tw.Write(" ");
                tw.Write("Group ");
                tw.Write(i);
                tw.WriteLine();
            }
        }

        private class PerGroupInfo
        {
            public int ActiveProcessorCount { get; set; }
            public int MaximumProcessorCount { get; set; }
            public IntPtr ActiveProcessorMask { get; set; }
        }

        private static readonly Lazy<List<PerGroupInfo>> s_data = new Lazy<List<PerGroupInfo>>(() => GetNativeInfo().ToList());

        private static IEnumerable<PerGroupInfo> GetNativeInfo()
        {
            var result = new List<PerGroupInfo>();
            NativeMethods.GetLogicalProcessorInformationEx<NativeMethods.SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_GROUP>(
                NativeMethods.RelationGroup,
                info =>
                {
                    for (int i = 0; i < info.Groups.ActiveGroupCount; i++)
                    {
                        var pgi = info.Groups.GroupInfo[i];
                        result.Add(new PerGroupInfo
                        {
                            ActiveProcessorMask = pgi.ActiveProcessorMask,
                            ActiveProcessorCount = pgi.ActiveProcessorCount,
                            MaximumProcessorCount = pgi.MaximumProcessorCount
                        });
                    }

                    // There should be only one group info item.
                    return false;
                });

            return result;
        }
    }
}