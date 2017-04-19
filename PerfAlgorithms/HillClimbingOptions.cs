using System;
using System.Threading;

namespace PerfAlgorithms
{
    public class HillClimbingOptions
    {
        public int SamplesToMeasure { get; private set; }

        public HillClimbingOptions()
        {
            Reset();
        }

        public void Reset()
        {
            // Defaults from : https://raw.githubusercontent.com/dotnet/coreclr/549c9960a8edcbe3930639e316616d35b22bca25/src/inc/clrconfigvalues.h
            WavePeriod = 4; //CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_WavePeriod);
            MaxThreadWaveMagnitude = 20; //CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_MaxWaveMagnitude);
            ThreadMagnitudeMultiplier = 100 / 100.0; //(double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_WaveMagnitudeMultiplier) / 100.0;
            SamplesToMeasure = WavePeriod * 8; //(int)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_WaveHistorySize);
            TargetThroughputRatio = 15 / 100.0; //(double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_Bias) / 100.0;
            TargetSignalToNoiseRatio = 300 / 100.0; // (double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_TargetSignalToNoiseRatio) / 100.0;
            MaxChangePerSecond = 4; // (double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_MaxChangePerSecond);
            MaxChangePerSample = 20; //(double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_MaxChangePerSample);
            SampleIntervalLow = 10; // CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_SampleIntervalLow);
            SampleIntervalHigh = 200; // CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_SampleIntervalHigh);
            ThroughputErrorSmoothingFactor = 1 / 100.0; //(double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_ErrorSmoothingFactor) / 100.0;
            GainExponent = 200 / 100.0; // (double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_GainExponent) / 100.0;
            MaxSampleError = 15 / 100.0; // (double)CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_MaxSampleErrorPercent) / 100.0;
        }

        /// <summary>
        /// Returns the number of currently required minimum threads.
        /// </summary>
        public Func<int> MinThreads { get; set; }

        /// <summary>
        /// Returns the number of currently allowed maximum threads.
        /// </summary>
        public Func<int> MaxThreads { get; set; }

        /// <summary>
        /// Returns the current CPU utilization in percent (e.g. 95 for 95%).
        /// </summary>
        public Func<int> CpuUtilization { get; set; }

        public int WavePeriod { get; set; }

        public double TargetThroughputRatio { get; set; }

        public double TargetSignalToNoiseRatio { get; set; }

        public double MaxChangePerSecond { get; set; }

        public double MaxChangePerSample { get; set; }

        public int MaxThreadWaveMagnitude { get; set; }

        public int SampleIntervalLow { get; set; }

        public double ThreadMagnitudeMultiplier { get; set; }

        public int SampleIntervalHigh { get; set; }

        public double ThroughputErrorSmoothingFactor { get; set; }

        public double GainExponent { get; set; }

        public double MaxSampleError { get; set; }

        private int TpMaxWorkerThreads
        {
            get
            {
                int worker, io;
                ThreadPool.GetMaxThreads(out worker, out io);
                return worker;
            }
        }

        private int TpMinWorkerThreads
        {
            get
            {
                int worker, io;
                ThreadPool.GetMinThreads(out worker, out io);
                return worker;
            }
        }

        internal int MinLimitThreads => MinThreads?.Invoke() ?? TpMinWorkerThreads;
        internal int MaxLimitThreads => MaxThreads?.Invoke() ?? TpMaxWorkerThreads;
        internal int CurrentCpuUtilization => CpuUtilization?.Invoke() ?? 0;

        internal void FireEtwThreadPoolWorkerThreadAdjustmentSample(double throughput)
        {
        }

        internal void FireEtwThreadPoolWorkerThreadAdjustmentAdjustment(int newThreadCount, 
            double throughput, HillClimbingStateTransition transition)
        {
        }

        internal void FireEtwThreadPoolWorkerThreadAdjustmentStats(double sampleDuration, double throughput, 
            double threadWaveComponent, double throughputWaveComponent, double throughputErrorEstimate, 
            double averageThroughputNoise, double ratio, double confidence, double currentControlSetting,
            ushort newThreadWaveMagnitude)
        {
        }
    }
}