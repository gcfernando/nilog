// -----------------------------------------------------------------------------
//  Nilog tests — 4-arg typed overloads: render correctness, structured state,
//  disabled-path zero allocation, and cache overflow protection.
//
//  File        : FourArgTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers the four-argument typed overloads and <see cref="Nilogger.MaxTemplateCacheEntries"/>.</summary>
public class FourArgTests
{
    // -------------------------------------------------------------------------
    // Static Log<T0,T1,T2,T3>
    // -------------------------------------------------------------------------

    [Fact]
    public void Log_FourTypedArgs_RendersAllValues()
    {
        TestLogger logger = new();

        Nilogger.Log(logger, LogLevel.Information, "{A} {B} {C} {D}", 1, 2, 3, 4);

        Assert.Equal("1 2 3 4", logger.Single.Message);
        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal(2, logger.Single["B"]);
        Assert.Equal(3, logger.Single["C"]);
        Assert.Equal(4, logger.Single["D"]);
        Assert.True(logger.Single.HasKey("{OriginalFormat}"));
    }

    [Fact]
    public void Log_FourTypedArgs_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        Nilogger.Log(logger, LogLevel.Debug, "{A} {B} {C} {D}", 1, 2, 3, 4);

        Assert.Empty(logger.Entries);
    }

    // -------------------------------------------------------------------------
    // WriteTrace/Debug/Information/Warning<T0,T1,T2,T3>
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteTrace_FourArgs_Renders()
    {
        TestLogger logger = new();
        logger.WriteTrace("t {A} {B} {C} {D}", 10, 20, 30, 40);

        Assert.Equal("t 10 20 30 40", logger.Single.Message);
        Assert.Equal(10, logger.Single["A"]);
        Assert.Equal(40, logger.Single["D"]);
    }

    [Fact]
    public void WriteDebug_FourArgs_Renders()
    {
        TestLogger logger = new();
        logger.WriteDebug("{A}-{B}-{C}-{D}", "w", "x", "y", "z");

        Assert.Equal("w-x-y-z", logger.Single.Message);
        Assert.Equal("w", logger.Single["A"]);
        Assert.Equal("z", logger.Single["D"]);
    }

    [Fact]
    public void WriteInformation_FourArgs_StructuredPropsAndOriginalFormat()
    {
        TestLogger logger = new();
        logger.WriteInformation("User {UserId} bought {Sku} x{Qty} in {Region}", 42, "A-100", 3, "EU");

        Assert.Equal("User 42 bought A-100 x3 in EU", logger.Single.Message);
        Assert.Equal(42, logger.Single["UserId"]);
        Assert.Equal("A-100", logger.Single["Sku"]);
        Assert.Equal(3, logger.Single["Qty"]);
        Assert.Equal("EU", logger.Single["Region"]);
        Assert.Equal("User {UserId} bought {Sku} x{Qty} in {Region}", logger.Single["{OriginalFormat}"]);
    }

    [Fact]
    public void WriteWarning_FourArgs_Renders()
    {
        TestLogger logger = new();
        logger.WriteWarning("{P1} {P2} {P3} {P4}", true, 1.5, 'c', 99L);

        Assert.Equal("True 1.5 c 99", logger.Single.Message);
    }

    // -------------------------------------------------------------------------
    // WriteError / WriteCritical with exception
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteError_FourArgsWithException_AttachesExceptionAndRenders()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("boom");

        logger.WriteError("Order {Id} failed for {User} at {Time} on {Host}", ex, 7, "alice", "now", "srv1");

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("Order 7 failed for alice at now on srv1", logger.Single.Message);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void WriteCritical_FourArgsWithException_AttachesException()
    {
        TestLogger logger = new();
        Exception ex = new("critical");

        logger.WriteCritical("{A} {B} {C} {D}", ex, "a", "b", "c", "d");

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("a b c d", logger.Single.Message);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    // -------------------------------------------------------------------------
    // WriteError / WriteCritical without exception
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteError_FourArgs_NoException_Renders()
    {
        TestLogger logger = new();
        logger.WriteError("Validation failed {F1} {F2} {F3} {F4}", "a", "b", "c", "d");

        Assert.Null(logger.Single.Exception);
        Assert.Equal("Validation failed a b c d", logger.Single.Message);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void WriteCritical_FourArgs_NoException_Renders()
    {
        TestLogger logger = new();
        logger.WriteCritical("{A} {B} {C} {D}", 1, 2, 3, 4);

        Assert.Null(logger.Single.Exception);
        Assert.Equal("1 2 3 4", logger.Single.Message);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    // -------------------------------------------------------------------------
    // Disabled-path zero allocation (CI regression guard)
    // -------------------------------------------------------------------------

    [Fact]
    public void DisabledPath_FourTypedArgs_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        // JIT warmup — ensure the method is compiled before we measure.
        for (int i = 0; i < 50; i++)
            logger.WriteDebug("User {Id} did {Action} in {Region} x{Count}", 42, "login", "us", 1);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
            logger.WriteDebug("User {Id} did {Action} in {Region} x{Count}", 42, "login", "us", 1);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }

    // -------------------------------------------------------------------------
    // MaxTemplateCacheEntries — cache overflow stops caching but still parses
    // -------------------------------------------------------------------------

    [Fact]
    public void MaxTemplateCacheEntries_WhenLimitReached_StillRendersCorrectly()
    {
        int savedLimit = Nilogger.MaxTemplateCacheEntries;
        try
        {
            // Use a very small limit so we can hit it without 10k templates.
            Nilogger.MaxTemplateCacheEntries = 5;

            TestLogger logger = new();

            // Fill the cache past the limit with distinct templates.
            for (int i = 0; i < 10; i++)
                logger.WriteInformation($"event_{i}_{{Value}}", i);

            // Templates beyond the limit must still render correctly (parsed on the fly).
            logger.WriteInformation("overflow {X} {Y} {Z} {W}", 1, 2, 3, 4);

            Assert.Equal("overflow 1 2 3 4", logger.Last.Message);
            Assert.Equal(1, logger.Last["X"]);
        }
        finally
        {
            Nilogger.MaxTemplateCacheEntries = savedLimit;
        }
    }
}
