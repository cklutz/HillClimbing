using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace PerfAlgorithms
{
    // Ported from https://github.com/dotnet/coreclr/blob/32f0f9721afb584b4a14d69135bea7ddc129f755/src/vm/hillclimbing.cpp.
    // MIT Licensed.

    public enum HillClimbingStateTransition
    {
        Warmup,
        Initializing,
        RandomMove,
        ClimbingMove,
        ChangePoint,
        Stabilizing,
        Starvation, //used by ThreadpoolMgr
        ThreadTimedOut, //used by ThreadpoolMgr
        Undefined,
    }

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

    public class HillClimbing
    {
        private readonly HillClimbingOptions m_options;

        private double m_currentControlSetting;
        private long m_totalSamples;
        private int m_lastThreadCount;
        private double m_elapsedSinceLastChange; //elapsed seconds since last thread count change
        private double m_completionsSinceLastChange; //number of completions since last thread count change

        private double m_averageThroughputNoise;

        private readonly double[] m_samples; //Circular buffer of the last m_samplesToMeasure samples
        private readonly double[] m_threadCounts; //Thread counts effective at each of m_samples

        private uint m_currentSampleInterval;
        private readonly Random m_randomIntervalGenerator;

        private int m_accumulatedCompletionCount;
        private double m_accumulatedSampleDuration;

        // From win32threadpool.h
        private const int CpuUtilizationHigh = 95;

        public HillClimbing()
            : this(new HillClimbingOptions())
        {
        }

        public HillClimbing(HillClimbingOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            m_options = options;
            m_samples = new double[m_options.SamplesToMeasure];
            m_threadCounts = new double[m_options.SamplesToMeasure];
            m_randomIntervalGenerator = new Random((AppDomain.CurrentDomain.Id << 16) ^ Process.GetCurrentProcess().Id);
            m_currentSampleInterval = (uint)m_randomIntervalGenerator.Next(m_options.SampleIntervalLow, m_options.SampleIntervalHigh + 1);
        }

        public int Update(int currentThreadCount, double sampleDuration, int numCompletions, out int pNewSampleInterval)
        {
            //
            // If someone changed the thread count without telling us, update our records accordingly.
            // 
            if (currentThreadCount != m_lastThreadCount)
                ForceChange(currentThreadCount, HillClimbingStateTransition.Initializing);

            //
            // Update the cumulative stats for this thread count
            //
            m_elapsedSinceLastChange += sampleDuration;
            m_completionsSinceLastChange += numCompletions;

            //
            // Add in any data we've already collected about this sample
            //
            sampleDuration += m_accumulatedSampleDuration;
            numCompletions += m_accumulatedCompletionCount;

            //
            // We need to make sure we're collecting reasonably accurate data.  Since we're just counting the end
            // of each work item, we are goinng to be missing some data about what really happened during the 
            // sample interval.  The count produced by each thread includes an initial work item that may have 
            // started well before the start of the interval, and each thread may have been running some new 
            // work item for some time before the end of the interval, which did not yet get counted.  So
            // our count is going to be off by +/- threadCount workitems.
            //
            // The exception is that the thread that reported to us last time definitely wasn't running any work
            // at that time, and the thread that's reporting now definitely isn't running a work item now.  So
            // we really only need to consider threadCount-1 threads.
            //
            // Thus the percent error in our count is +/- (threadCount-1)/numCompletions.
            //
            // We cannot rely on the frequency-domain analysis we'll be doing later to filter out this error, because
            // of the way it accumulates over time.  If this sample is off by, say, 33% in the negative direction,
            // then the next one likely will be too.  The one after that will include the sum of the completions
            // we missed in the previous samples, and so will be 33% positive.  So every three samples we'll have 
            // two "low" samples and one "high" sample.  This will appear as periodic variation right in the frequency
            // range we're targeting, which will not be filtered by the frequency-domain translation.
            //
            if (m_totalSamples > 0 && ((currentThreadCount - 1.0) / numCompletions) >= m_options.MaxSampleError)
            {
                // not accurate enough yet.  Let's accumulate the data so far, and tell the ThreadPool
                // to collect a little more.
                m_accumulatedSampleDuration = sampleDuration;
                m_accumulatedCompletionCount = numCompletions;
                pNewSampleInterval = 10;
                return currentThreadCount;
            }

            //
            // We've got enouugh data for our sample; reset our accumulators for next time.
            //
            m_accumulatedSampleDuration = 0;
            m_accumulatedCompletionCount = 0;

            //
            // Add the current thread count and throughput sample to our history
            //
            double throughput = (double)numCompletions / sampleDuration;
            m_options.FireEtwThreadPoolWorkerThreadAdjustmentSample(throughput);

            int sampleIndex = (int)(m_totalSamples % m_options.SamplesToMeasure);
            m_samples[sampleIndex] = throughput;
            m_threadCounts[sampleIndex] = currentThreadCount;
            m_totalSamples++;

            //
            // Set up defaults for our metrics
            //
            Complex threadWaveComponent = 0;
            Complex throughputWaveComponent = 0;
            double throughputErrorEstimate = 0;
            Complex ratio = 0;
            double confidence = 0;

            HillClimbingStateTransition transition = HillClimbingStateTransition.Warmup;

            //
            // How many samples will we use?  It must be at least the three wave periods we're looking for, and it must also be a whole
            // multiple of the primary wave's period; otherwise the frequency we're looking for will fall between two  frequency bands 
            // in the Fourier analysis, and we won't be able to measure it accurately.
            // 
            int sampleCount = ((int)Math.Min(m_totalSamples - 1, m_options.SamplesToMeasure) / m_options.WavePeriod) * m_options.WavePeriod;

            if (sampleCount > m_options.WavePeriod)
            {
                //
                // Average the throughput and thread count samples, so we can scale the wave magnitudes later.
                //
                double sampleSum = 0;
                double threadSum = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    sampleSum += m_samples[(m_totalSamples - sampleCount + i) % m_options.SamplesToMeasure];
                    threadSum += m_threadCounts[(m_totalSamples - sampleCount + i) % m_options.SamplesToMeasure];
                }
                double averageThroughput = sampleSum / sampleCount;
                double averageThreadCount = threadSum / sampleCount;

                if (averageThroughput > 0 && averageThreadCount > 0)
                {
                    //
                    // Calculate the periods of the adjacent frequency bands we'll be using to measure noise levels.
                    // We want the two adjacent Fourier frequency bands.
                    //
                    double adjacentPeriod1 = sampleCount / (((double)sampleCount / (double)m_options.WavePeriod) + 1);
                    double adjacentPeriod2 = sampleCount / (((double)sampleCount / (double)m_options.WavePeriod) - 1);

                    //
                    // Get the the three different frequency components of the throughput (scaled by average
                    // throughput).  Our "error" estimate (the amount of noise that might be present in the
                    // frequency band we're really interested in) is the average of the adjacent bands.
                    //
                    throughputWaveComponent = GetWaveComponent(m_samples, sampleCount, m_options.WavePeriod) / averageThroughput;
                    throughputErrorEstimate = Complex.Abs(GetWaveComponent(m_samples, sampleCount, adjacentPeriod1) / averageThroughput);
                    if (adjacentPeriod2 <= sampleCount)
                        throughputErrorEstimate = Math.Max(throughputErrorEstimate, Complex.Abs(GetWaveComponent(m_samples, sampleCount, adjacentPeriod2) / averageThroughput));

                    //
                    // Do the same for the thread counts, so we have something to compare to.  We don't measure thread count
                    // noise, because there is none; these are exact measurements.
                    //
                    threadWaveComponent = GetWaveComponent(m_threadCounts, sampleCount, m_options.WavePeriod) / averageThreadCount;

                    //
                    // Update our moving average of the throughput noise.  We'll use this later as feedback to
                    // determine the new size of the thread wave.
                    //
                    if (m_averageThroughputNoise == 0)
                        m_averageThroughputNoise = throughputErrorEstimate;
                    else
                        m_averageThroughputNoise = (m_options.ThroughputErrorSmoothingFactor * throughputErrorEstimate) + ((1.0 - m_options.ThroughputErrorSmoothingFactor) * m_averageThroughputNoise);

                    if (Complex.Abs(threadWaveComponent) > 0)
                    {
                        //
                        // Adjust the throughput wave so it's centered around the target wave, and then calculate the adjusted throughput/thread ratio.
                        //
                        ratio = (throughputWaveComponent - (m_options.TargetThroughputRatio * threadWaveComponent)) / threadWaveComponent;
                        transition = HillClimbingStateTransition.ClimbingMove;
                    }
                    else
                    {
                        ratio = 0;
                        transition = HillClimbingStateTransition.Stabilizing;
                    }

                    //
                    // Calculate how confident we are in the ratio.  More noise == less confident.  This has
                    // the effect of slowing down movements that might be affected by random noise.
                    //
                    double noiseForConfidence = Math.Max(m_averageThroughputNoise, throughputErrorEstimate);
                    if (noiseForConfidence > 0)
                        confidence = (Complex.Abs(threadWaveComponent) / noiseForConfidence) / m_options.TargetSignalToNoiseRatio;
                    else
                        confidence = 1.0; //there is no noise!

                }
            }

            //
            // We use just the real part of the complex ratio we just calculated.  If the throughput signal
            // is exactly in phase with the thread signal, this will be the same as taking the magnitude of
            // the complex move and moving that far up.  If they're 180 degrees out of phase, we'll move
            // backward (because this indicates that our changes are having the opposite of the intended effect).
            // If they're 90 degrees out of phase, we won't move at all, because we can't tell wether we're
            // having a negative or positive effect on throughput.
            //
            double move = Math.Min(1.0, Math.Max(-1.0, ratio.Real));

            //
            // Apply our confidence multiplier.
            //
            move *= Math.Min(1.0, Math.Max(0.0, confidence));

            //
            // Now apply non-linear gain, such that values around zero are attenuated, while higher values
            // are enhanced.  This allows us to move quickly if we're far away from the target, but more slowly
            // if we're getting close, giving us rapid ramp-up without wild oscillations around the target.
            // 
            double gain = m_options.MaxChangePerSecond * sampleDuration;
            move = Math.Pow(Math.Abs(move), m_options.GainExponent) * (move >= 0.0 ? 1 : -1) * gain;
            move = Math.Min(move, m_options.MaxChangePerSample);

            //
            // If the result was positive, and CPU is > 95%, refuse the move.
            //
            if (move > 0.0 && m_options.CurrentCpuUtilization > CpuUtilizationHigh)
                move = 0.0;

            //
            // Apply the move to our control setting
            // 
            m_currentControlSetting += move;

            //
            // Calculate the new thread wave magnitude, which is based on the moving average we've been keeping of
            // the throughput error.  This average starts at zero, so we'll start with a nice safe little wave at first.
            //
            int newThreadWaveMagnitude = (int)(0.5 + (m_currentControlSetting * m_averageThroughputNoise * m_options.TargetSignalToNoiseRatio * m_options.ThreadMagnitudeMultiplier * 2.0));
            newThreadWaveMagnitude = Math.Min(newThreadWaveMagnitude, m_options.MaxThreadWaveMagnitude);
            newThreadWaveMagnitude = Math.Max(newThreadWaveMagnitude, 1);

            //
            // Make sure our control setting is within the ThreadPool's limits
            // 
            m_currentControlSetting = Math.Min(m_options.MaxLimitThreads - newThreadWaveMagnitude, m_currentControlSetting);
            m_currentControlSetting = Math.Max(m_options.MinLimitThreads, m_currentControlSetting);

            //
            // Calculate the new thread count (control setting + square wave)
            // 
            int newThreadCount = (int)(m_currentControlSetting + newThreadWaveMagnitude * ((m_totalSamples / (m_options.WavePeriod / 2)) % 2));

            //
            // Make sure the new thread count doesn't exceed the ThreadPool's limits
            // 
            newThreadCount = Math.Min(m_options.MaxLimitThreads, newThreadCount);
            newThreadCount = Math.Max(m_options.MinLimitThreads, newThreadCount);

            //
            // Record these numbers for posterity
            //
            m_options.FireEtwThreadPoolWorkerThreadAdjustmentStats(
                sampleDuration, 
                throughput, 
                threadWaveComponent.Real, 
                throughputWaveComponent.Real, 
                throughputErrorEstimate, 
                m_averageThroughputNoise,
                ratio.Real,
                confidence,
                m_currentControlSetting, 
                (ushort)newThreadWaveMagnitude);

            //
            // If all of this caused an actual change in thread count, log that as well.
            // 
            if (newThreadCount != currentThreadCount)
                ChangeThreadCount(newThreadCount, transition);

            //
            // Return the new thread count and sample interval.  This is randomized to prevent correlations with other periodic
            // changes in throughput.  Among other things, this prevents us from getting confused by Hill Climbing instances
            // running in other processes.
            //
            // If we're at minThreads, and we seem to be hurting performance by going higher, we can't go any lower to fix this.  So
            // we'll simply stay at minThreads much longer, and only occasionally try a higher value.
            //
            if (ratio.Real < 0.0 && newThreadCount == m_options.MinLimitThreads)
                pNewSampleInterval = (int)(0.5 + m_currentSampleInterval * (10.0 * Math.Max(-ratio.Real, 1.0)));
            else
                pNewSampleInterval = (int)m_currentSampleInterval;

            return newThreadCount;
        }

        public void ForceChange(int newThreadCount, HillClimbingStateTransition transition)
        {
            if (newThreadCount != m_lastThreadCount)
            {
                m_currentControlSetting += (newThreadCount - m_lastThreadCount);
                ChangeThreadCount(newThreadCount, transition);
            }
        }

        private void ChangeThreadCount(int newThreadCount, HillClimbingStateTransition transition)
        {
            m_lastThreadCount = newThreadCount;
            m_currentSampleInterval = (uint)m_randomIntervalGenerator.Next(m_options.SampleIntervalLow, m_options.SampleIntervalHigh + 1);
            double throughput = (m_elapsedSinceLastChange > 0) ? (m_completionsSinceLastChange / m_elapsedSinceLastChange) : 0;
            m_options.FireEtwThreadPoolWorkerThreadAdjustmentAdjustment(newThreadCount, throughput, transition);
            m_elapsedSinceLastChange = 0;
            m_completionsSinceLastChange = 0;
        }

        private Complex GetWaveComponent(double[] samples, int sampleCount, double period)
        {
            Debug.Assert(sampleCount >= period, "sampleCount >= period"); //can't measure a wave that doesn't fit
            Debug.Assert(period >= 2, "period >= 2"); //can't measure above the Nyquist frequency

            const double pi = 3.141592653589793;

            //
            // Calculate the sinusoid with the given period.
            // We're using the Goertzel algorithm for this.  See http://en.wikipedia.org/wiki/Goertzel_algorithm.
            //
            double w = 2.0 * pi / period;
            double cosine = Math.Cos(w);
            double sine = Math.Sin(w);
            double coeff = 2.0 * cosine;
            double q0 = 0, q1 = 0, q2 = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                double sample = samples[(m_totalSamples - sampleCount + i) % m_options.SamplesToMeasure];

                q0 = coeff * q1 - q2 + sample;
                q2 = q1;
                q1 = q0;
            }

            return new Complex(q1 - q2 * cosine, q2 * sine) / (double)sampleCount;
        }
    }
}