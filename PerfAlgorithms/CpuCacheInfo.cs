using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PerfAlgorithms
{
    public class CpuCacheInfo
    {
        public static void Dump(TextWriter tw)
        {
            // Somewhat simulate the output of sysinternals' "coreinfo.exe -l"

            var native = GetNativeInfo();

            int maxMask = native.Max(n => Convert.ToString(n.GroupMask.Mask.ToInt32(), 2).Length);
            int maxType = native.Max(n => n.Type.ToString().Length);
            int maxSize = native.Max(n => (n.CacheSize / 1024.0).ToString("N").Length);
            int maxAsso = native.Max(n => n.Associativity.ToString().Length);

            foreach (var entry in native)
            {
                string map = new string(Convert.ToString(entry.GroupMask.Mask.ToInt32(), 2).Reverse().ToArray());
                tw.Write(map.Replace('1', '*').Replace('0', '-').PadRight(maxMask, '-'));
                tw.Write(" ");
                tw.Write(entry.Type.ToString().PadRight(maxType));
                tw.Write(" ");
                tw.Write(entry.GroupMask.Group);
                tw.Write(", ");
                tw.Write("Level ");
                tw.Write(entry.Level);
                tw.Write(", ");
                tw.Write((entry.CacheSize / 1024.0).ToString("N0").PadLeft(maxSize));
                tw.Write(" KB, ");
                tw.Write("Assoc ");
                tw.Write(entry.Associativity.ToString().PadLeft(maxAsso));
                tw.Write(", ");
                tw.Write("LineSize ");
                tw.Write(entry.LineSize);
                tw.WriteLine();
            }
        }

        private static IEnumerable<NativeMethods.CACHE_RELATIONSHIP> GetNativeInfo()
        {
            var result = new List<NativeMethods.CACHE_RELATIONSHIP>();
            NativeMethods.GetLogicalProcessorInformationEx<NativeMethods.SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_CACHE>(
                NativeMethods.RelationCache,
                info =>
                {
                    result.Add(info.Cache);
                    // There are multiple entries, one for each cache.
                    return true;
                });

            return result;
        }
    }
}