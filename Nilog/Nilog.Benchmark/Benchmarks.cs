// -----------------------------------------------------------------------------
//  Nilog benchmarks — BenchmarkDotNet suites comparing Nilog with
//  Microsoft.Extensions.Logging across every public surface and argument count.
//
//  File        : Benchmarks.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Logging;

namespace Nilog.Benchmark;

// =============================================================================
//  Each class isolates one dimension of logging cost: argument count, enabled vs
//  disabled, scope shape, exception formatting, template features, and sustained
//  throughput. The MemoryDiagnoser reports bytes allocated per operation so the
//  zero-allocation claim on disabled levels can be verified numerically.
// =============================================================================

// ---------------------------------------------------------------------------
// 1. NO-ARG (plain message) — the LoggerMessage.Define pre-compiled path
// ---------------------------------------------------------------------------

/// <summary>
/// Zero-template-argument path.  Nilog uses pre-compiled <c>LoggerMessage.Define</c>
/// delegates so the enabled case pays no dictionary lookup or struct construction.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class NoArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Baseline = true, Description = "Microsoft .LogInformation")]
    public void Microsoft_Enabled() => _enabled.LogInformation("Service started");

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Nilog .WriteInformation")]
    public void Nilog_Enabled() => _enabled.WriteInformation("Service started");

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft .LogDebug")]
    public void Microsoft_Disabled() => _disabled.LogDebug("Service started");

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog .WriteDebug")]
    public void Nilog_Disabled() => _disabled.WriteDebug("Service started");
}

// ---------------------------------------------------------------------------
// 2. ONE-ARG — enabled vs disabled
// ---------------------------------------------------------------------------

/// <summary>Single strongly-typed argument: the most common structured-log pattern.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OneArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Enabled() => _enabled.LogInformation("User {Id} signed in", 42);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Nilog")]
    public void Nilog_Enabled() => _enabled.WriteInformation("User {Id} signed in", 42);

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Disabled() => _disabled.LogInformation("User {Id} signed in", 42);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog")]
    public void Nilog_Disabled() => _disabled.WriteInformation("User {Id} signed in", 42);
}

// ---------------------------------------------------------------------------
// 3. TWO-ARG — enabled vs disabled
// ---------------------------------------------------------------------------

/// <summary>
/// Two strongly-typed arguments. The standard case uses <c>int + decimal</c>: decimal is a
/// 16-byte value type that boxes to a 24-byte heap object, larger than an <c>int</c> (4-byte
/// value, 16-byte boxed object). This explains why the 2-arg enabled path can appear slower
/// than 3-arg or 4-arg paths that use only <c>int</c> arguments — it is heavier per-boxing,
/// not a code-path regression. The "AllInts" category uses two <c>int</c> args to confirm
/// the code path itself is identical to the 3/4-arg path; only the decimal boxing cost differs.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class TwoArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled (int+decimal)"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Enabled() => _enabled.LogInformation("Order {Id} total {Amount}", 42, 129.95m);

    [BenchmarkCategory("Enabled (int+decimal)"), Benchmark(Description = "Nilog")]
    public void Nilog_Enabled() => _enabled.WriteInformation("Order {Id} total {Amount}", 42, 129.95m);

    [BenchmarkCategory("Enabled (int+int)"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Enabled_Ints() => _enabled.LogInformation("Order {Id} {Count}", 42, 7);

    [BenchmarkCategory("Enabled (int+int)"), Benchmark(Description = "Nilog — same path as 3/4-arg")]
    public void Nilog_Enabled_Ints() => _enabled.WriteInformation("Order {Id} {Count}", 42, 7);

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Disabled() => _disabled.LogInformation("Order {Id} total {Amount}", 42, 129.95m);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog — 0 B expected")]
    public void Nilog_Disabled() => _disabled.WriteInformation("Order {Id} total {Amount}", 42, 129.95m);
}

// ---------------------------------------------------------------------------
// 4. THREE-ARG — enabled vs disabled
// ---------------------------------------------------------------------------

/// <summary>Three strongly-typed arguments — squarely in the middle of the typed overload range (0–5 args).</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ThreeArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Enabled() => _enabled.LogInformation("{A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Nilog")]
    public void Nilog_Enabled() => _enabled.WriteInformation("{A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void Microsoft_Disabled() => _disabled.LogInformation("{A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog")]
    public void Nilog_Disabled() => _disabled.WriteInformation("{A} {B} {C}", 1, 2, 3);
}

// ---------------------------------------------------------------------------
// 5. FOUR-ARG TYPED — new zero-array path (was params in v1.0.x)
// ---------------------------------------------------------------------------

/// <summary>
/// Four arguments now bind to the strongly-typed <c>WriteInformation&lt;T0,T1,T2,T3&gt;</c>
/// overload — no <c>object[]</c> on the disabled path, and the same struct-based rendering
/// as the 1–3 arg paths on the enabled path. Microsoft still allocates the array.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class FourArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Baseline = true, Description = "Microsoft 4 args (params)")]
    public void Microsoft_Enabled() => _enabled.LogInformation("{A} {B} {C} {D}", 1, 2, 3, 4);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Nilog 4 args (typed — zero array)")]
    public void Nilog_Enabled() => _enabled.WriteInformation("{A} {B} {C} {D}", 1, 2, 3, 4);

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft 4 args (params)")]
    public void Microsoft_Disabled() => _disabled.LogInformation("{A} {B} {C} {D}", 1, 2, 3, 4);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog 4 args (typed — 0 B expected)")]
    public void Nilog_Disabled() => _disabled.WriteInformation("{A} {B} {C} {D}", 1, 2, 3, 4);
}

// ---------------------------------------------------------------------------
// 5b. FIVE-ARG TYPED — new zero-array path (NEW in v1.0.2, was params before)
// ---------------------------------------------------------------------------

/// <summary>
/// Five arguments now bind to the strongly-typed <c>WriteInformation&lt;T0,T1,T2,T3,T4&gt;</c>
/// overload (added in v1.0.2) — no <c>object[]</c> on the disabled path, and the same
/// struct-based rendering as the 1–4 arg paths on the enabled path. Microsoft still
/// allocates the array.
/// </summary>
/// <remarks>
/// <b>Enabled-path note:</b> the 5-arg code path is structurally identical to the 3-arg and
/// 4-arg paths (same span-based Render, same AggressiveInlining). Any apparent slowdown vs
/// Microsoft seen in earlier benchmark runs was measured with an attached debugger, which
/// artificially inflates Nilog numbers. Rerun without the debugger to see accurate numbers.
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class FiveArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Baseline = true, Description = "Microsoft 5 args (params)")]
    public void Microsoft_Enabled() => _enabled.LogInformation("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Nilog 5 args (typed — zero array)")]
    public void Nilog_Enabled() => _enabled.WriteInformation("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft 5 args (params)")]
    public void Microsoft_Disabled() => _disabled.LogInformation("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog 5 args (typed — 0 B expected)")]
    public void Nilog_Disabled() => _disabled.WriteInformation("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);
}

// ---------------------------------------------------------------------------
// 5c. SIX-TO-EIGHT ARG TYPED — source-generated zero-array path (NEW in v1.0.3)
// ---------------------------------------------------------------------------

/// <summary>
/// Six, seven, and eight arguments now bind to source-generated strongly-typed
/// <c>WriteInformation&lt;T0..Tn&gt;</c> overloads (Nilog.SourceGenerators, new in v1.0.3) —
/// no <c>object[]</c> on the disabled path, same struct-based state as the 1–5 arg paths.
/// Microsoft still allocates the array on every call. The disabled rows must show 0 B.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class HighArityTypedBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("6 args"), Benchmark(Baseline = true, Description = "Microsoft (params)")]
    public void MS_Six() => _enabled.LogInformation("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

    [BenchmarkCategory("6 args"), Benchmark(Description = "Nilog (typed — zero array)")]
    public void Nilog_Six() => _enabled.WriteInformation("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

    [BenchmarkCategory("8 args"), Benchmark(Baseline = true, Description = "Microsoft (params)")]
    public void MS_Eight() => _enabled.LogInformation("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

    [BenchmarkCategory("8 args"), Benchmark(Description = "Nilog (typed — zero array)")]
    public void Nilog_Eight() => _enabled.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

    [BenchmarkCategory("8 args disabled"), Benchmark(Baseline = true, Description = "Microsoft (params)")]
    public void MS_Eight_Disabled() => _disabled.LogDebug("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

    [BenchmarkCategory("8 args disabled"), Benchmark(Description = "Nilog (typed — 0 B expected)")]
    public void Nilog_Eight_Disabled() => _disabled.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);
}

// ---------------------------------------------------------------------------
// 5d. PARAMS PATH (9+ args) — the true open-ended escape hatch
// ---------------------------------------------------------------------------

/// <summary>
/// Nine arguments: Nilog's source generator now covers arities 6–16, so a 9-arg call binds
/// to the typed <c>WriteInformation&lt;T0..T8&gt;</c> overload rather than the params path.
/// The disabled row must show 0 B. Microsoft still allocates the array on both paths.
/// This class is kept for direct comparison against Microsoft at 9 args.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ParamsPathBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Baseline = true, Description = "Microsoft 9 args (params)")]
    public void Microsoft_Enabled() => _enabled.LogInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Nilog 9 args (typed — zero array)")]
    public void Nilog_Enabled() => _enabled.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("Disabled"), Benchmark(Baseline = true, Description = "Microsoft 9 args (params)")]
    public void Microsoft_Disabled() => _disabled.LogInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Nilog 9 args (typed — 0 B expected)")]
    public void Nilog_Disabled() => _disabled.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);
}

// ---------------------------------------------------------------------------
// 6. DISABLED — all argument counts in one place (the zero-allocation proof)
// ---------------------------------------------------------------------------

/// <summary>
/// Disabled path across every argument count.  Every Nilog row must show 0 B allocated;
/// Microsoft rows show the increasing allocation cost of building unused arrays.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class DisabledAllArgsBenchmarks
{
    private readonly BenchLogger _ms = new(enabled: false);
    private readonly BenchLogger _nl = new(enabled: false);

    [BenchmarkCategory("0 args"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Zero() => _ms.LogDebug("static message");

    [BenchmarkCategory("0 args"), Benchmark(Description = "Nilog")]
    public void Nilog_Zero() => _nl.WriteDebug("static message");

    [BenchmarkCategory("1 arg"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_One() => _ms.LogDebug("User {Id}", 42);

    [BenchmarkCategory("1 arg"), Benchmark(Description = "Nilog")]
    public void Nilog_One() => _nl.WriteDebug("User {Id}", 42);

    [BenchmarkCategory("2 args"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Two() => _ms.LogDebug("Order {Id} {Amount}", 42, 99.99m);

    [BenchmarkCategory("2 args"), Benchmark(Description = "Nilog")]
    public void Nilog_Two() => _nl.WriteDebug("Order {Id} {Amount}", 42, 99.99m);

    [BenchmarkCategory("3 args"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Three() => _ms.LogDebug("Event {A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("3 args"), Benchmark(Description = "Nilog")]
    public void Nilog_Three() => _nl.WriteDebug("Event {A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("4 args (typed)"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Four() => _ms.LogDebug("{A} {B} {C} {D}", 1, 2, 3, 4);

    [BenchmarkCategory("4 args (typed)"), Benchmark(Description = "Nilog — 0 B expected")]
    public void Nilog_Four() => _nl.WriteDebug("{A} {B} {C} {D}", 1, 2, 3, 4);

    [BenchmarkCategory("5 args (typed)"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Five() => _ms.LogDebug("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("5 args (typed)"), Benchmark(Description = "Nilog — 0 B expected")]
    public void Nilog_Five() => _nl.WriteDebug("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("6 args (typed)"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Six() => _ms.LogDebug("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

    [BenchmarkCategory("6 args (typed)"), Benchmark(Description = "Nilog — 0 B expected")]
    public void Nilog_Six() => _nl.WriteDebug("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

    [BenchmarkCategory("8 args (typed)"), Benchmark(Baseline = true, Description = "Microsoft")]
    public void MS_Eight() => _ms.LogDebug("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

    [BenchmarkCategory("8 args (typed)"), Benchmark(Description = "Nilog — 0 B expected")]
    public void Nilog_Eight() => _nl.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

    [BenchmarkCategory("9 args (typed)"), Benchmark(Baseline = true, Description = "Microsoft (params)")]
    public void MS_Nine() => _ms.LogDebug("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("9 args (typed)"), Benchmark(Description = "Nilog — 0 B expected (typed overload)")]
    public void Nilog_Nine() => _nl.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);
}

// ---------------------------------------------------------------------------
// 7. EXCEPTION LOGGING
// ---------------------------------------------------------------------------

/// <summary>
/// Exception attachment and formatted exception reports.
/// The basic report (<c>moreDetailsEnabled: false</c>) uses a compact single-line format
/// targeting under 300 B per call. The full report (<c>moreDetailsEnabled: true</c>) is a
/// cold-path feature and intentionally allocates more — it builds a multi-line verbose block
/// with stack trace and inner exceptions.
/// </summary>
[MemoryDiagnoser]
public class ExceptionBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);
    private static readonly Exception _ex = Build();

    [Benchmark(Baseline = true, Description = "WriteError typed 1-arg + exception")]
    public void ErrorWithException_Typed() => _logger.WriteError("Failed for {Id}", _ex, 7);

    [Benchmark(Description = "WriteError typed 1-arg no exception")]
    public void ErrorTyped_NoException() => _logger.WriteError("Failed job {JobId}", 7);

    [Benchmark(Description = "WriteError message + exception (no-args, Feature C)")]
    public void ErrorWithException_NoArgs() => _logger.WriteError("Payment gateway timeout", _ex);

    [Benchmark(Description = "WriteErrorException report (basic)")]
    public void ReportBasic() => _logger.WriteErrorException(_ex, "Error", moreDetailsEnabled: false);

    [Benchmark(Description = "WriteErrorException report (full — stack + inner)")]
    public void ReportFull() => _logger.WriteErrorException(_ex, "Error", moreDetailsEnabled: true);

    [Benchmark(Description = "WriteCriticalException report (basic)")]
    public void CriticalReportBasic() => _logger.WriteCriticalException(_ex, "Critical", moreDetailsEnabled: false);

    private static Exception Build()
    {
        try
        {
            try { throw new KeyNotFoundException("inner"); }
            catch (Exception inner) { throw new InvalidOperationException("outer", inner); }
        }
        catch (Exception caught) { return caught; }
    }
}

// ---------------------------------------------------------------------------
// 8. SCOPE — all shapes and sizes
// ---------------------------------------------------------------------------

/// <summary>
/// Scope creation across every shape: single key/value, dictionary at 1 / 4 / 8 entries,
/// and IReadOnlyDictionary (Feature A IEnumerable path).
/// </summary>
[MemoryDiagnoser]
public class ScopeAllShapesBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);

    private readonly Dictionary<string, object> _dict1 = new() { ["A"] = 1 };
    private readonly Dictionary<string, object> _dict3 = new()
    {
        ["UserId"] = 42, ["Tenant"] = "acme", ["Region"] = "eu-west-1",
    };
    private readonly Dictionary<string, object> _dict4 = new()
    {
        ["A"] = 1, ["B"] = 2, ["C"] = 3, ["D"] = 4,
    };
    private readonly Dictionary<string, object> _dict8;
    private readonly IReadOnlyDictionary<string, object> _readOnly;

    public ScopeAllShapesBenchmarks()
    {
        _dict8 = [];
        for (int i = 0; i < 8; i++) _dict8[$"K{i}"] = i;
        _readOnly = new Dictionary<string, object>
        {
            ["TraceId"] = "4bf92f3577b34da6a3ce929d0e0e4736",
            ["SpanId"] = "00f067aa0ba902b7",
        };
    }

    [Benchmark(Baseline = true, Description = "Single key/value (SingleScope)")]
    public void Scope_Single() { using IDisposable s = _logger.WriteScope("RequestId", 42); }

    [Benchmark(Description = "IDictionary 1 entry (SmallScopeWrapper)")]
    public void Scope_Dict1() { using IDisposable s = _logger.WriteScope(_dict1); }

    [Benchmark(Description = "IDictionary 3 entries (SmallScopeWrapper)")]
    public void Scope_Dict3() { using IDisposable s = _logger.WriteScope(_dict3); }

    [Benchmark(Description = "IDictionary 4 entries (SmallScopeWrapper max)")]
    public void Scope_Dict4() { using IDisposable s = _logger.WriteScope(_dict4); }

    [Benchmark(Description = "IDictionary 8 entries (ScopeWrapper)")]
    public void Scope_Dict8() { using IDisposable s = _logger.WriteScope(_dict8); }

    [Benchmark(Description = "IReadOnlyDictionary 2 entries (Feature A)")]
    public void Scope_ReadOnly() { using IDisposable s = _logger.WriteScope(_readOnly); }
}

// ---------------------------------------------------------------------------
// 9. TEMPLATE FEATURES — format specifiers, alignment, escaped braces
// ---------------------------------------------------------------------------

/// <summary>
/// Template parsing is a one-time cost (cached after first use), so these benchmarks
/// measure the warm-cache rendering: string.Format with various format strings.
/// </summary>
[MemoryDiagnoser]
public class TemplateBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);

    [Benchmark(Baseline = true, Description = "Plain named placeholder {Id}")]
    public void Plain() => _logger.WriteInformation("User {Id} signed in", 42);

    [Benchmark(Description = "Format specifier {Amount:N2}")]
    public void FormatSpecifier() => _logger.WriteInformation("Revenue {Amount:N2}", 18_450.75m);

    [Benchmark(Description = "Column alignment {Label,-20}")]
    public void Alignment() => _logger.WriteInformation("[{Label,-20}] {Count,8:N0}", "orders", 12500);

    [Benchmark(Description = "Escaped braces {{literal}} + placeholder")]
    public void EscapedBraces() => _logger.WriteInformation("Template {{OrderId}} → {OrderId}", 7);

    [Benchmark(Description = "3 args with format suffixes")]
    public void ThreeArgsFormatted() => _logger.WriteInformation("{A:N0} {B:P1} {C}", 99999, 0.0123, "ok");
}

// ---------------------------------------------------------------------------
// 10. RUNTIME-LEVEL API — Nilogger.Log with level decided at call time
// ---------------------------------------------------------------------------

/// <summary>
/// The static <c>Nilogger.Log</c> overloads (level chosen at runtime) across argument
/// counts. Verifies that the runtime-level dispatch adds no meaningful overhead.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class RuntimeLevelBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Log 0 args")]
    public void Log_Zero_Enabled() => Nilogger.Log(_enabled, LogLevel.Information, "started");

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Log 1 arg")]
    public void Log_One_Enabled() => Nilogger.Log(_enabled, LogLevel.Warning, "User {Id}", 42);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Log 3 args")]
    public void Log_Three_Enabled() => Nilogger.Log(_enabled, LogLevel.Error, "{A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Log 4 args (typed)")]
    public void Log_Four_Enabled() => Nilogger.Log(_enabled, LogLevel.Error, "{A} {B} {C} {D}", 1, 2, 3, 4);

    // 5- and 8-arg static Log: proves the v1.0.3 overload-priority fix keeps these on the
    // zero-array typed path (before v1.0.3 they silently bound to params object[]).
    [BenchmarkCategory("Enabled"), Benchmark(Description = "Log 5 args (typed — fixed v1.0.3)")]
    public void Log_Five_Enabled() => Nilogger.Log(_enabled, LogLevel.Error, "{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("Enabled"), Benchmark(Description = "Log 8 args (typed — fixed v1.0.3)")]
    public void Log_Eight_Enabled() => Nilogger.Log(_enabled, LogLevel.Error, "{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Log 0 args")]
    public void Log_Zero_Disabled() => Nilogger.Log(_disabled, LogLevel.Information, "started");

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Log 1 arg")]
    public void Log_One_Disabled() => Nilogger.Log(_disabled, LogLevel.Warning, "User {Id}", 42);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Log 3 args")]
    public void Log_Three_Disabled() => Nilogger.Log(_disabled, LogLevel.Error, "{A} {B} {C}", 1, 2, 3);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Log 4 args (typed — 0 B)")]
    public void Log_Four_Disabled() => Nilogger.Log(_disabled, LogLevel.Error, "{A} {B} {C} {D}", 1, 2, 3, 4);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Log 5 args (typed — 0 B, fixed v1.0.3)")]
    public void Log_Five_Disabled() => Nilogger.Log(_disabled, LogLevel.Error, "{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

    [BenchmarkCategory("Disabled"), Benchmark(Description = "Log 8 args (typed — 0 B, fixed v1.0.3)")]
    public void Log_Eight_Disabled() => Nilogger.Log(_disabled, LogLevel.Error, "{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);
}

// ---------------------------------------------------------------------------
// 11. FLUSH — async sink drain overhead
// ---------------------------------------------------------------------------

/// <summary>
/// FlushAsync with no registered callbacks stays a zero-allocation no-op: returns
/// <see cref="Task.CompletedTask"/> synchronously. (When buffering sinks register via
/// Nilogger.RegisterFlush, FlushAsync awaits them instead — not measured here.)
/// </summary>
[MemoryDiagnoser]
public class FlushBenchmarks
{
    [Benchmark(Baseline = true, Description = "FlushAsync() — returns Task.CompletedTask")]
    public Task FlushAsync_Normal() => Nilogger.FlushAsync();

    [Benchmark(Description = "FlushAsync(cancelledToken) — same no-op")]
    public Task FlushAsync_Cancelled()
        => Nilogger.FlushAsync(new CancellationToken(canceled: true));
}

// ---------------------------------------------------------------------------
// 12. PARALLEL THROUGHPUT — all cores writing concurrently
// ---------------------------------------------------------------------------

/// <summary>Throughput under contention: all cores logging at once.</summary>
[MemoryDiagnoser]
public class ParallelBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);

    [Params(50_000)]
    public int N;

    [Benchmark(Baseline = true, Description = "Microsoft (parallel)")]
    public void Microsoft_Parallel()
        => Parallel.For(0, N, i => _logger.LogInformation("worker {Id} tick", i));

    [Benchmark(Description = "Nilog typed (parallel)")]
    public void Nilog_Parallel()
        => Parallel.For(0, N, i => _logger.WriteInformation("worker {Id} tick", i));
}

// ---------------------------------------------------------------------------
// 13. SUSTAINED THROUGHPUT — tight single-thread loop
// ---------------------------------------------------------------------------

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
            _logger.LogInformation("event {Id} value {Value}", i, i * 2);
    }

    [Benchmark(Description = "Nilog typed (loop)")]
    public void Nilog_Loop()
    {
        for (int i = 0; i < N; i++)
            _logger.WriteInformation("event {Id} value {Value}", i, i * 2);
    }
}

// ---------------------------------------------------------------------------
// 14. ALLOCATION STRESS — cumulative bytes over N calls
// ---------------------------------------------------------------------------

/// <summary>
/// Total allocation over N calls to prove the per-call profile at scale.
/// Disabled Nilog rows must show 0 B; enabled rows must show only unavoidable boxing.
/// Microsoft disabled rows show the params-array tax even when the entry is discarded.
/// </summary>
[MemoryDiagnoser]
public class AllocationStressBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [Params(10_000)]
    public int N;

    [Benchmark(Baseline = true, Description = "Nilog disabled 3-arg typed (0 alloc)")]
    public void Nilog_Disabled_NoAlloc()
    {
        for (int i = 0; i < N; i++)
            _disabled.WriteInformation("event {Id} value {Val}", i, i * 2L);
    }

    [Benchmark(Description = "Nilog disabled 4-arg typed (0 alloc)")]
    public void Nilog_Disabled_4Arg_NoAlloc()
    {
        for (int i = 0; i < N; i++)
            _disabled.WriteInformation("event {A} {B} {C} {D}", i, i * 2L, i * 3L, i * 4L);
    }

    [Benchmark(Description = "Nilog disabled 5-arg typed (0 alloc — new in v1.0.2)")]
    public void Nilog_Disabled_5Arg_NoAlloc()
    {
        for (int i = 0; i < N; i++)
            _disabled.WriteInformation("event {A} {B} {C} {D} {E}", i, i * 2L, i * 3L, i * 4L, i * 5L);
    }

    [Benchmark(Description = "Microsoft disabled 3-arg (params array each call)")]
    public void Microsoft_Disabled_Allocs()
    {
        for (int i = 0; i < N; i++)
            _disabled.LogInformation("event {Id} value {Val}", i, i * 2L);
    }

    [Benchmark(Description = "Microsoft disabled 4-arg (params array each call)")]
    public void Microsoft_Disabled_4Arg_Allocs()
    {
        for (int i = 0; i < N; i++)
            _disabled.LogInformation("event {A} {B} {C} {D}", i, i * 2L, i * 3L, i * 4L);
    }

    [Benchmark(Description = "Microsoft disabled 5-arg (params array each call)")]
    public void Microsoft_Disabled_5Arg_Allocs()
    {
        for (int i = 0; i < N; i++)
            _disabled.LogInformation("event {A} {B} {C} {D} {E}", i, i * 2L, i * 3L, i * 4L, i * 5L);
    }

    [Benchmark(Description = "Nilog enabled 1-arg typed")]
    public void Nilog_Enabled_Typed1()
    {
        for (int i = 0; i < N; i++)
            _enabled.WriteInformation("event {Id}", i);
    }

    [Benchmark(Description = "Nilog enabled 3-arg typed")]
    public void Nilog_Enabled_Typed3()
    {
        for (int i = 0; i < N; i++)
            _enabled.WriteInformation("event {A} {B} {C}", i, i * 2, i * 3);
    }

    [Benchmark(Description = "Nilog enabled 4-arg typed")]
    public void Nilog_Enabled_Typed4()
    {
        for (int i = 0; i < N; i++)
            _enabled.WriteInformation("event {A} {B} {C} {D}", i, i * 2, i * 3, i * 4);
    }

    [Benchmark(Description = "Nilog enabled 5-arg typed (new in v1.0.2)")]
    public void Nilog_Enabled_Typed5()
    {
        for (int i = 0; i < N; i++)
            _enabled.WriteInformation("event {A} {B} {C} {D} {E}", i, i * 2, i * 3, i * 4, i * 5);
    }

    [Benchmark(Description = "Microsoft enabled 3-arg (params)")]
    public void Microsoft_Enabled_Params3()
    {
        for (int i = 0; i < N; i++)
            _enabled.LogInformation("event {A} {B} {C}", i, i * 2, i * 3);
    }

    [Benchmark(Description = "Microsoft enabled 4-arg (params)")]
    public void Microsoft_Enabled_Params4()
    {
        for (int i = 0; i < N; i++)
            _enabled.LogInformation("event {A} {B} {C} {D}", i, i * 2, i * 3, i * 4);
    }

    [Benchmark(Description = "Microsoft enabled 5-arg (params)")]
    public void Microsoft_Enabled_Params5()
    {
        for (int i = 0; i < N; i++)
            _enabled.LogInformation("event {A} {B} {C} {D} {E}", i, i * 2, i * 3, i * 4, i * 5);
    }
}

// ---------------------------------------------------------------------------
// 15. TEMPLATE CACHE — cold vs warm parse cost
// ---------------------------------------------------------------------------

[MemoryDiagnoser]
public class TemplateCacheBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);
    private static readonly string _fixed = "User {Id} signed in";

    [Benchmark(Baseline = true, Description = "Warm cache — same literal template every call")]
    public void WarmCache() => _logger.WriteInformation(_fixed, 42);

    [Benchmark(Description = "Cold parse — unique template per call (simulates misuse)")]
    public void ColdParse() => _logger.WriteInformation($"User {Guid.NewGuid()} signed in at {DateTime.UtcNow}", 0);
}

// ---------------------------------------------------------------------------
// 16. VALUE VS REFERENCE ARG — value-type vs reference-type argument cost
// ---------------------------------------------------------------------------

[MemoryDiagnoser]
public class ValueVsReferenceArgBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);
    private static readonly string _strArg = "hello";

    [Benchmark(Baseline = true, Description = "3 int args (value type)")]
    public void ThreeInts_Enabled() => _enabled.WriteInformation("{A} {B} {C}", 1, 2, 3);

    [Benchmark(Description = "3 string args (reference type)")]
    public void ThreeStrings_Enabled() => _enabled.WriteInformation("{A} {B} {C}", _strArg, _strArg, _strArg);

    [Benchmark(Description = "Mixed: int, decimal, string")]
    public void Mixed_Enabled() => _enabled.WriteInformation("{A} {B} {C}", 42, 9.99m, _strArg);

    [Benchmark(Description = "3 int args disabled (0 B expected)")]
    public void ThreeInts_Disabled() => _disabled.WriteDebug("{A} {B} {C}", 1, 2, 3);
}

// ---------------------------------------------------------------------------
// 17. TYPED SCOPE — typed scope overloads vs dictionary
// ---------------------------------------------------------------------------

[MemoryDiagnoser]
public class TypedScopeBenchmarks
{
    private readonly BenchLogger _logger = new(enabled: true);
    private readonly Dictionary<string, object> _dict2 = new() { ["UserId"] = 42, ["Tenant"] = "acme" };

    [Benchmark(Baseline = true, Description = "Single key/value (existing)")]
    public void Single() { using IDisposable s = _logger.WriteScope("RequestId", 42); }

    [Benchmark(Description = "2-pair typed WriteScope<T1,T2>")]
    public void TwoPairTyped() { using IDisposable s = _logger.WriteScope("UserId", 42, "Tenant", "acme"); }

    [Benchmark(Description = "2-pair IDictionary (SmallScopeWrapper)")]
    public void TwoPairDict() { using IDisposable s = _logger.WriteScope(_dict2); }

    [Benchmark(Description = "3-pair typed WriteScope<T1,T2,T3>")]
    public void ThreePairTyped() { using IDisposable s = _logger.WriteScope("UserId", 42, "Tenant", "acme", "Region", "eu-west-1"); }
}

// ---------------------------------------------------------------------------
// 18. HIGH ARITY EXTENDED — 9-arg typed overload (new in v1.0.4)
// ---------------------------------------------------------------------------

[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class HighArityExtendedBenchmarks
{
    private readonly BenchLogger _enabled = new(enabled: true);
    private readonly BenchLogger _disabled = new(enabled: false);

    [BenchmarkCategory("9 args"), Benchmark(Baseline = true, Description = "Microsoft (params)")]
    public void MS_Nine() => _enabled.LogInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("9 args"), Benchmark(Description = "Nilog 9 args (typed — zero array)")]
    public void Nilog_Nine() => _enabled.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("9 args disabled"), Benchmark(Baseline = true, Description = "Microsoft (params)")]
    public void MS_Nine_Disabled() => _disabled.LogDebug("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);

    [BenchmarkCategory("9 args disabled"), Benchmark(Description = "Nilog 9 args (typed — 0 B expected)")]
    public void Nilog_Nine_Disabled() => _disabled.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H} {I}", 1, 2, 3, 4, 5, 6, 7, 8, 9);
}
