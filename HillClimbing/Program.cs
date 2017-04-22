using System;
using System.Globalization;
using System.IO;
using System.Threading;
using PerfAlgorithms;

namespace HillClimbing
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                // Dump some information about available CPU caches (just because we can).
                // Of course, the CPU cache has "some" influence, but is not really relevant
                // for the stuff below. Really, it should move into some (to be created)
                // utility library.
                CpuCacheInfo.Dump(Console.Out);
                // Dump some information about available CPU groups and CLR settings.
                CpuGroupInfo.Dump(Console.Out);
                // Start (background) collection of CPU utilization.
                CpuUtilizationHelper.Initialize(500);

                // Start (background) threads to utilize cpu for approx. percentage
                BurnCpu(percentage: 50);

                // To convert these CSV files to graphs, do:
                // 1.) Install Microsoft R Client.
                // 2.) Run R GUI and execute "install.package('ggplot2')"
                // 3.) Run the following command on each file:
                //
                // "C:\Program Files\Microsoft\R Client\R_SERVER\bin\x64\rscript.exe" CreateGraphs.R <CSV> <PNG>
                //
                TestRun(true, "results-random.csv", CpuUtilizationHelper.GetCpuUtilization);
                TestRun(false, "results-smooth.csv", CpuUtilizationHelper.GetCpuUtilization);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return ex.HResult;
            }
        }

        static void BurnCpu(int percentage)
        {
            int cpus = Environment.ProcessorCount;
            int numThreads = 1;
            if (cpus > 1)
            {
                numThreads = cpus * percentage / 100;
                if (numThreads == 0)
                    numThreads = 1;
            }

            var threads = new Thread[numThreads];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    while (true)
                    {
                        // Nothing, just waste cycles like crazy.
                    }
                });
                threads[i].IsBackground = true;
                threads[i].Start();
            }
        }

        static void TestRun(bool randomWorkloadJumps, string fileName, Func<int> cpuUtilization)
        {
            // Ported from http://mattwarren.org/2017/04/13/The-CLR-Thread-Pool-Thread-Injection-Algorithm/

            Console.WriteLine("Running Hill Climbing algorithm ({0})", fileName);

            var options = new HillClimbingOptions();
            options.MinThreads = () => 2;
            options.MaxThreads = () => 1000;
            options.CpuUtilization = cpuUtilization;

            var hc = new PerfAlgorithms.HillClimbing(options);

            int newMax = 0, threadAdjustmentInterval = 0;
            long totalCompletions = 0, priorCompletionCount = 0;
            int timer = 0, lastSampleTimer = 0;

            int currentThreadCount = options.MinThreads();
            hc.ForceChange(currentThreadCount, HillClimbingStateTransition.Initializing);

            var randomGenerator = new Random();

            using (var fp = new StreamWriter(fileName))
            {
                fp.WriteLine("Time,Throughput,Threads");
                for (int mode = 1; mode <= 5; mode++)
                {
                    int currentWorkLoad = 0;
                    switch (mode)
                    {
                        case 1:
                        case 5:
                            currentWorkLoad = 3;
                            break;
                        case 2:
                        case 4:
                            currentWorkLoad = 7;
                            break;
                        case 3:
                            currentWorkLoad = 10;
                            break;
                        default:
                            currentWorkLoad = 1;
                            break;
                    }

                    bool reportedMsgInWorkload = false;
                    int workLoadForSection = currentWorkLoad * 500;
                    while (workLoadForSection > 0)
                    {
                        if (randomWorkloadJumps)
                        {
                            int randomValue = randomGenerator.Next(21); // 0 to 20
                            if (randomValue >= 19)
                            {
                                int randomChange = randomGenerator.Next(-2, 3); // i.e. -2, -1, 0, 1, 2 (not 3)
                                if (randomChange != 0)
                                {
                                    Console.WriteLine("Changing workload from {0} -> {1}\n", currentWorkLoad, currentWorkLoad + randomChange);
                                    currentWorkLoad += randomChange;
                                }
                            }
                        }
                        timer += 1; //tick-tock, each iteration of the loop is 1 second
                        totalCompletions += currentThreadCount;
                        workLoadForSection -= currentThreadCount;
                        //fprintf(fp, "%d,%d\n", min(currentWorkLoad, currentThreadCount), currentThreadCount);
                        double randomNoise = randomGenerator.NextDouble() / 100.0 * 5; // [0..1) -> [0..0.01) -> [0..0.05)
                        fp.WriteLine("{0},{1},{2}", timer, (Math.Min(currentWorkLoad, currentThreadCount) * (0.95 + randomNoise)).ToString(CultureInfo.InvariantCulture), currentThreadCount);
                        // Calling HillClimbingInstance.Update(..) should ONLY happen when we need more threads, not all the time!!
                        if (currentThreadCount != currentWorkLoad)
                        {
                            // We naively assume that each work items takes 1 second (which is also our loop/timer length)
                            // So in every loop we complete 'currentThreadCount' pieces of work
                            int numCompletions = currentThreadCount;

                            // In win32threadpool.cpp it does the following check before calling Update(..)
                            // if (elapsed*1000.0 >= (ThreadAdjustmentInterval/2)) // 
                            // Also 'ThreadAdjustmentInterval' is initially set like so ('INTERNAL_HillClimbing_SampleIntervalLow' = 10):
                            // ThreadAdjustmentInterval = CLRConfig::GetConfigValue(CLRConfig::INTERNAL_HillClimbing_SampleIntervalLow);
                            double sampleDuration = (double)(timer - lastSampleTimer);
                            if (sampleDuration * 1000.0 >= (threadAdjustmentInterval / 2))
                            {
                                newMax = hc.Update(currentThreadCount, sampleDuration, numCompletions, out threadAdjustmentInterval);
                                Console.WriteLine("Mode = {0}, Num Completions = {1} ({2}), New Max = {3} (Old = {4}), threadAdjustmentInterval = {5}",
                                    mode, numCompletions, totalCompletions, newMax, currentThreadCount, threadAdjustmentInterval);

                                if (newMax > currentThreadCount)
                                {
                                    // Never go beyound what we actually need (plus 1)
                                    int newThreadCount = Math.Min(newMax, currentWorkLoad + 1); // + 1
                                    if (newThreadCount != 0 && newThreadCount > currentThreadCount)
                                    {
                                        // We only ever increase by 1 at a time!
                                        Console.WriteLine("*** INCREASING thread count, from {0} -> {1} (CurrentWorkLoad = {2}, Hill-Climbing New Max = {3})***",
                                            currentThreadCount, currentThreadCount + 1, currentWorkLoad, newMax);
                                        currentThreadCount += 1;
                                    }
                                    else
                                    {
                                        Console.WriteLine("*** SHOULD HAVE INCREASED thread count, but didn't, newMax = {0}, currentThreadCount = {1}, currentWorkLoad = {2}",
                                            newMax, currentThreadCount, currentWorkLoad);
                                    }
                                }
                                else if (newMax < (currentThreadCount - 1) && newMax != 0)
                                {
                                    Console.WriteLine("*** DECREASING thread count, from {0} -> {1} (CurrentWorkLoad = {2}, Hill-Climbing New Max = {3})***",
                                        currentThreadCount, currentThreadCount - 1, currentWorkLoad, newMax);
                                    currentThreadCount -= 1;
                                }

                                priorCompletionCount = totalCompletions;
                                lastSampleTimer = timer;
                            }
                            else
                            {
                                Console.WriteLine("Sample Duration is too small, current = {0}, needed = {1} (threadAdjustmentInterval = {2})",
                                    sampleDuration, (threadAdjustmentInterval / 2) / 1000.0, threadAdjustmentInterval);
                            }
                        }
                        else
                        {
                            if (reportedMsgInWorkload == false)
                            {
                                Console.WriteLine("Enough threads to carry out current workload, currentThreadCount = {0}, currentWorkLoad= {1}\n",
                                    currentThreadCount, currentWorkLoad);
                            }

                            reportedMsgInWorkload = true;
                        }
                    }

                    fp.Flush();
                }
            }
        }
    }

}