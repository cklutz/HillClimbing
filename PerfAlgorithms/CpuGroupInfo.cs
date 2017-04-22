using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace PerfAlgorithms
{
    public class CpuGroupInfo
    {
        public static bool CanEnableGCCpuGroups()
        {
            return s_data.Value.EnableGCCpuGroups && s_data.Value.HasMultileGroups;
        }

        public static bool CanEnableThreadUseAllCpuGroups()
        {
            return s_data.Value.ThreadsUseAllCpuGroups && s_data.Value.HasMultileGroups;
        }

        public static int NumberOfProcessors => s_data.Value.NumberOfProcessors;

        public static void Dump(TextWriter tw)
        {
            tw.WriteLine("EnableGCCpuGroups = {0}", s_data.Value.EnableGCCpuGroups);
            tw.WriteLine("ThreadsUseAllCpuGroups = {0}", s_data.Value.ThreadsUseAllCpuGroups);
            tw.WriteLine("HasMultileGroups = {0}", s_data.Value.HasMultileGroups);
            tw.WriteLine("GroupCount = {0}", s_data.Value.GroupCount);
            tw.WriteLine("NumberOfProcessors = {0}", s_data.Value.NumberOfProcessors);
            foreach (var group in s_data.Value.PerGroupInfo)
            {
                tw.WriteLine("Group:");
                tw.WriteLine("    MaximumProcessorCount = {0}", group.MaximumProcessorCount);
                tw.WriteLine("    ActiveProcessorCount = {0}", group.ActiveProcessorCount);
                tw.WriteLine("    ActiveProcessorMask = {0}", Convert.ToString(group.ActiveProcessorMask.ToInt64(), 2));
            }
        }

        private class CpuGroupData
        {
            public bool HasMultileGroups => GroupCount > 1;
            public bool EnableGCCpuGroups { get; set; }
            public bool ThreadsUseAllCpuGroups { get; set; }
            public int GroupCount => PerGroupInfo.Count;
            public List<PerGroupInfo> PerGroupInfo { get; set; }
            public int NumberOfProcessors { get; set; }
        }

        private class PerGroupInfo
        {
            public int ActiveProcessorCount { get; set; }
            public int MaximumProcessorCount { get; set; }
            public IntPtr ActiveProcessorMask { get; set; }
        }

        private static readonly Lazy<XmlElement> s_runtimeSection = new Lazy<XmlElement>(() =>
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                string rawXml = config.GetSection("runtime")?.SectionInformation.GetRawXml();
                if (!string.IsNullOrEmpty(rawXml))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(rawXml);
                    if (doc.DocumentElement != null)
                        return doc.DocumentElement;
                }
                return null;
            }
            catch (Exception ex)
            {
                Trace.Write(ex);
                return null;
            }
        });

        private static readonly Lazy<CpuGroupData> s_data = new Lazy<CpuGroupData>(() =>
        {
            var data = new CpuGroupData();
            data.PerGroupInfo = GetNativeInfo().ToList();
            data.NumberOfProcessors = data.PerGroupInfo.Sum(pg => pg.ActiveProcessorCount);
            data.EnableGCCpuGroups = GetClrConfigValue("GCCpuGroup");
            data.ThreadsUseAllCpuGroups = GetClrConfigValue("Thread_UseAllCpuGroups");
            return data;
        });

        private static bool GetClrConfigValue(string switchName)
        {
            // A poor man's version of the CLRConfig native class in the CLR.
            // The real thing, does a lot more things, e.g. one can say wether
            // to prefer env over app.config over registry, or other variations
            // thereof.
            // Note also that this version currently ignores the registry.

            string env = Environment.GetEnvironmentVariable("COMPLUS_" + switchName);
            if (env != null)
            {
                bool flag;
                if (Boolean.TryParse(env, out flag))
                    return flag;
            }

            var enabled = s_runtimeSection.Value?
                .ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(n => n.Name.Equals(switchName, StringComparison.OrdinalIgnoreCase))
                ?
                .GetAttribute("enabled");

            if (!string.IsNullOrEmpty(enabled))
            {
                bool flag;
                if (Boolean.TryParse(enabled, out flag))
                    return flag;
            }

            return false;
        }

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