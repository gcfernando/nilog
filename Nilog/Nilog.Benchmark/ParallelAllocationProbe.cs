// -----------------------------------------------------------------------------
//  Nilog benchmarks — process-wide allocation probe for concurrent logging (F-003).
//
//  Why this exists: BenchmarkDotNet's allocation column for a Parallel.For benchmark mixes the
//  logging cost with the scheduler's own allocations (delegates, tasks, partitioner state) and
//  its attribution across worker threads is not a per-call logging figure. This probe isolates
//  the logging work: N dedicated threads (no Parallel.For) each perform the identical logging
//  call a fixed number of times, and the process-wide GC.GetTotalAllocatedBytes(precise: true)
//  delta is divided by the number of calls. An empty-body control run (same threads, same loop,
//  no logging call) is subtracted so thread start/loop overhead is not attributed to logging.
//
//  Run:  dotnet run -c Release -f net10.0 --project Nilog.Benchmark -- --parallel-alloc [threads] [callsPerThread] [launches]
// -----------------------------------------------------------------------------
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Nilog.Benchmark;

public static class ParallelAllocationProbe
{
    public static int Run(string[] args)
    {
        int threads = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : Environment.ProcessorCount;
        int calls = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 200_000;
        int launches = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 10;

        Console.WriteLine($"# ParallelAllocationProbe threads={threads} callsPerThread={calls} launches={launches} runtime={Environment.Version} os={Environment.OSVersion}");
        Console.WriteLine("scenario,launch,bytesPerCall,nsPerCallPerThread");

        BenchLogger logger = new(enabled: true);
        (string Name, Action<int> Body)[] scenarios =
        [
            ("control-empty", static _ => { }),
            ("microsoft-LogInformation-1arg", i => logger.LogInformation("worker {Id} tick", i)),
            ("nilog-WriteInformation-1arg", i => logger.WriteInformation("worker {Id} tick", i)),
            ("microsoft-LogInformation-3arg", i => logger.LogInformation("a {A} b {B} c {C}", i, "x", 3L)),
            ("nilog-WriteInformation-3arg", i => logger.WriteInformation("a {A} b {B} c {C}", i, "x", 3L)),
        ];

        foreach ((string name, Action<int> body) in scenarios)
        {
            Measure(threads, 2_000, body); // warm-up: JIT, template cache, tiered compilation
            Measure(threads, calls / 4, body);
        }

        Dictionary<string, List<double>> bytes = [];
        for (int l = 0; l < launches; l++)
        {
            double control = 0;
            foreach ((string name, Action<int> body) in scenarios)
            {
                (long allocated, double ns) = Measure(threads, calls, body);
                double perCall = (double)allocated / ((long)threads * calls);
                if (name == "control-empty")
                {
                    control = perCall;
                }

                double net = name == "control-empty" ? perCall : perCall - control;
                Console.WriteLine($"{name},{l},{net.ToString("F2", CultureInfo.InvariantCulture)},{ns.ToString("F1", CultureInfo.InvariantCulture)}");
                if (!bytes.TryGetValue(name, out List<double>? list))
                {
                    bytes[name] = list = [];
                }

                list.Add(net);
            }
        }

        Console.WriteLine("# summary (net bytes/call, control subtracted)");
        foreach ((string name, List<double> v) in bytes)
        {
            v.Sort();
            double mean = v.Average();
            Console.WriteLine($"# {name}: median={v[v.Count / 2].ToString("F2", CultureInfo.InvariantCulture)} min={v[0].ToString("F2", CultureInfo.InvariantCulture)} max={v[^1].ToString("F2", CultureInfo.InvariantCulture)} mean={mean.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        return 0;
    }

    private static (long Allocated, double NsPerCall) Measure(int threads, int calls, Action<int> body)
    {
        using Barrier ready = new(threads + 1);
        using ManualResetEventSlim go = new(false);
        Thread[] ts = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            ts[t] = new Thread(() =>
            {
                _ = ready.SignalAndWait(TimeSpan.FromSeconds(30));
                go.Wait();
                for (int i = 0; i < calls; i++)
                {
                    body(i);
                }
            })
            { IsBackground = true };
            ts[t].Start();
        }

        _ = ready.SignalAndWait(TimeSpan.FromSeconds(30));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        go.Set();
        foreach (Thread t in ts)
        {
            t.Join();
        }

        double elapsedNs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1_000_000.0;
        long after = GC.GetTotalAllocatedBytes(precise: true);
        return (after - before, elapsedNs / calls);
    }
}
