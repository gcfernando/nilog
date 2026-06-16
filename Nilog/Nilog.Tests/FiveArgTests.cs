// -----------------------------------------------------------------------------
//  Nilog tests — 5-arg typed overloads: render correctness, structured state,
//  disabled-path zero allocation, and the Exception-overload resolution guard.
//
//  File        : FiveArgTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers the five-argument typed overloads, mirroring <see cref="FourArgTests"/>.</summary>
public class FiveArgTests
{
    // -------------------------------------------------------------------------
    // Static Log<T0,T1,T2,T3,T4>
    // -------------------------------------------------------------------------

    [Fact]
    public void Log_FiveTypedArgs_RendersAllValues()
    {
        TestLogger logger = new();

        Nilogger.Log(logger, LogLevel.Information, "{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

        Assert.Equal("1 2 3 4 5", logger.Single.Message);
        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal(5, logger.Single["E"]);
        Assert.True(logger.Single.HasKey("{OriginalFormat}"));
    }

    [Fact]
    public void Log_FiveTypedArgs_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        Nilogger.Log(logger, LogLevel.Debug, "{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Log_FiveArgs_WithLeadingException_StillBindsToExceptionOverload()
    {
        // Regression guard: Log<T0,T1,T2,T3,T4> is a normal-form match for exactly
        // 5 trailing arguments, same total count as Log(message, exception, params[4]).
        // Without [OverloadResolutionPriority(-1)] on the generic overload, a call with
        // a leading Exception value would silently bind to the generic overload instead
        // (treating the exception as a plain template value) because normal-form beats
        // expanded-params form in C# overload resolution.
        TestLogger logger = new();
        InvalidOperationException ex = new("boom");

        Nilogger.Log(logger, LogLevel.Error, "a {0} {1} {2} {3}", ex, 1, 2, 3, 4);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("a 1 2 3 4", logger.Single.Message);
    }

    // -------------------------------------------------------------------------
    // WriteTrace/Debug/Information/Warning<T0,T1,T2,T3,T4>
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteTrace_FiveArgs_Renders()
    {
        TestLogger logger = new();
        logger.WriteTrace("t {A} {B} {C} {D} {E}", 10, 20, 30, 40, 50);

        Assert.Equal("t 10 20 30 40 50", logger.Single.Message);
        Assert.Equal(10, logger.Single["A"]);
        Assert.Equal(50, logger.Single["E"]);
    }

    [Fact]
    public void WriteDebug_FiveArgs_Renders()
    {
        TestLogger logger = new();
        logger.WriteDebug("{A}-{B}-{C}-{D}-{E}", "v", "w", "x", "y", "z");

        Assert.Equal("v-w-x-y-z", logger.Single.Message);
        Assert.Equal("v", logger.Single["A"]);
        Assert.Equal("z", logger.Single["E"]);
    }

    [Fact]
    public void WriteInformation_FiveArgs_StructuredPropsAndOriginalFormat()
    {
        TestLogger logger = new();
        logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel}", 42, "A-100", 3, "EU", "web");

        Assert.Equal("User 42 bought A-100 x3 in EU via web", logger.Single.Message);
        Assert.Equal(42, logger.Single["UserId"]);
        Assert.Equal("A-100", logger.Single["Sku"]);
        Assert.Equal(3, logger.Single["Qty"]);
        Assert.Equal("EU", logger.Single["Region"]);
        Assert.Equal("web", logger.Single["Channel"]);
        Assert.Equal("User {UserId} bought {Sku} x{Qty} in {Region} via {Channel}", logger.Single["{OriginalFormat}"]);
    }

    [Fact]
    public void WriteWarning_FiveArgs_Renders()
    {
        TestLogger logger = new();
        logger.WriteWarning("{P1} {P2} {P3} {P4} {P5}", true, 1.5, 'c', 99L, "x");

        Assert.Equal("True 1.5 c 99 x", logger.Single.Message);
    }

    // -------------------------------------------------------------------------
    // WriteError / WriteCritical with exception
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteError_FiveArgsWithException_AttachesExceptionAndRenders()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("boom");

        logger.WriteError("Order {Id} failed for {User} at {Time} on {Host} in {Region}", ex, 7, "alice", "now", "srv1", "eu-west-1");

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("Order 7 failed for alice at now on srv1 in eu-west-1", logger.Single.Message);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void WriteCritical_FiveArgsWithException_AttachesException()
    {
        TestLogger logger = new();
        Exception ex = new("critical");

        logger.WriteCritical("{A} {B} {C} {D} {E}", ex, "a", "b", "c", "d", "e");

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("a b c d e", logger.Single.Message);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    // -------------------------------------------------------------------------
    // WriteError / WriteCritical without exception
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteError_FiveArgs_NoException_Renders()
    {
        TestLogger logger = new();
        logger.WriteError("Validation failed {F1} {F2} {F3} {F4} {F5}", "a", "b", "c", "d", "e");

        Assert.Null(logger.Single.Exception);
        Assert.Equal("Validation failed a b c d e", logger.Single.Message);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void WriteCritical_FiveArgs_NoException_Renders()
    {
        TestLogger logger = new();
        logger.WriteCritical("{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5);

        Assert.Null(logger.Single.Exception);
        Assert.Equal("1 2 3 4 5", logger.Single.Message);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    // -------------------------------------------------------------------------
    // Disabled-path zero allocation (CI regression guard) — the actual point of
    // adding 5-arg typed overloads: this used to allocate a params object[5].
    // -------------------------------------------------------------------------

    [Fact]
    public void DisabledPath_FiveTypedArgs_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        // JIT warmup — ensure the method is compiled before we measure.
        for (int i = 0; i < 50; i++)
            logger.WriteDebug("User {Id} did {Action} in {Region} x{Count} via {Channel}", 42, "login", "us", 1, "web");

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
            logger.WriteDebug("User {Id} did {Action} in {Region} x{Count} via {Channel}", 42, "login", "us", 1, "web");

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }
}
