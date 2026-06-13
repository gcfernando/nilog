// -----------------------------------------------------------------------------
//  Nilog benchmarks — the BenchmarkDotNet suites (enabled, disabled, exception,
//  scope, parallel, stress) comparing Nilog with Microsoft.Extensions.Logging.
//
//  File        : Benchmarks.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Logging;

namespace Nilog.Benchmark;

// =============================================================================
//  Each class below isolates one dimension of logging cost. Nilog's typed
//  overloads are compared head-to-head with the framework's params-based
//  extension methods, so the numbers show exactly what the strongly-typed path
//  buys you - in time and, crucially, in allocations.
// =============================================================================

/// <summary>Enabled hot path: the sink is listening, so every entry is fully rendered.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class EnabledBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);

    [BenchmarkCategory("1 arg"), Benchmark(Baseline = true, Description = "Microsoft .LogInformation")]
    public void Microsoft_OneArg()
    {
        _logger.LogInformation("User {Id} signed in", 42);
    }

    [BenchmarkCategory("1 arg"), Benchmark(Description = "Nilog .WriteInformation")]
    public void Nilog_OneArg()
    {
        _logger.WriteInformation("User {Id} signed in", 42);
    }

    [BenchmarkCategory("3 args"), Benchmark(Baseline = true, Description = "Microsoft .LogInformation")]
    public void Microsoft_ThreeArgs()
    {
        _logger.LogInformation("{A} {B} {C}", 1, 2, 3);
    }

    [BenchmarkCategory("3 args"), Benchmark(Description = "Nilog .WriteInformation")]
    public void Nilog_ThreeArgs()
    {
        _logger.WriteInformation("{A} {B} {C}", 1, 2, 3);
    }
}

/// <summary>
/// Disabled hot path: the level is filtered out. This is where Nilog shines - the typed
/// overloads allocate nothing, while the framework's params overloads still build the
/// object[] and box the value types before discovering the entry is unwanted.
/// </summary>
[MemoryDiagnoser]
public class DisabledBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: false);

    [Benchmark(Baseline = true, Description = "Microsoft (disabled)")]
    public void Microsoft_Disabled()
    {
        _logger.LogInformation("User {Id} at {Ticks} from {Ip}", 42, 638_000_000_000_000_000L, "10.0.0.1");
    }

    [Benchmark(Description = "Nilog typed (disabled)")]
    public void Nilog_Disabled()
    {
        _logger.WriteInformation("User {Id} at {Ticks} from {Ip}", 42, 638_000_000_000_000_000L, "10.0.0.1");
    }
}

/// <summary>Exception logging: attaching an exception and producing a formatted report.</summary>
[MemoryDiagnoser]
public class ExceptionBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);
    private static readonly Exception _ex = Build();

    [Benchmark(Description = "Error + exception + arg")]
    public void ErrorWithException()
    {
        _logger.WriteError("Failed for {Id}", _ex, 7);
    }

    [Benchmark(Description = "Exception report (basic)")]
    public void ReportBasic()
    {
        _logger.WriteErrorException(_ex, "Error", moreDetailsEnabled: false);
    }

    [Benchmark(Description = "Exception report (full)")]
    public void ReportFull()
    {
        _logger.WriteErrorException(_ex, "Error", moreDetailsEnabled: true);
    }

    private static Exception Build()
    {
        try
        {
            try
            {
                throw new KeyNotFoundException("inner");
            }
            catch (Exception inner)
            {
                throw new InvalidOperationException("outer", inner);
            }
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}

/// <summary>Scope creation cost for the single-pair and dictionary forms.</summary>
[MemoryDiagnoser]
public class ScopeBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);
    private readonly Dictionary<string, object> _context = new()
    {
        ["UserId"] = 42,
        ["Tenant"] = "acme",
        ["Region"] = "eu-west-1",
    };

    [Benchmark(Description = "Single key/value scope")]
    public void SingleScope()
    {
        using IDisposable scope = _logger.WriteScope("RequestId", 42);
    }

    [Benchmark(Description = "Dictionary scope (3 entries)")]
    public void DictionaryScope()
    {
        using IDisposable scope = _logger.WriteScope(_context);
    }
}

/// <summary>Throughput under contention: all cores logging at once.</summary>
[MemoryDiagnoser]
public class ParallelBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);

    [Params(50_000)]
    public int N;

    [Benchmark(Baseline = true, Description = "Microsoft (parallel)")]
    public void Microsoft_Parallel()
    {
        _ = Parallel.For(0, N, i => _logger.LogInformation("worker {Id} tick", i));
    }

    [Benchmark(Description = "Nilog typed (parallel)")]
    public void Nilog_Parallel()
    {
        _ = Parallel.For(0, N, i => _logger.WriteInformation("worker {Id} tick", i));
    }
}

/// <summary>Sustained single-thread volume: a tight loop of structured entries.</summary>
[MemoryDiagnoser]
public class StressBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);

    [Params(100_000)]
    public int N;

    [Benchmark(Baseline = true, Description = "Microsoft (loop)")]
    public void Microsoft_Loop()
    {
        for (int i = 0; i < N; i++)
        {
            _logger.LogInformation("event {Id} value {Value}", i, i * 2);
        }
    }

    [Benchmark(Description = "Nilog typed (loop)")]
    public void Nilog_Loop()
    {
        for (int i = 0; i < N; i++)
        {
            _logger.WriteInformation("event {Id} value {Value}", i, i * 2);
        }
    }
}
