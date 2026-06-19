// -----------------------------------------------------------------------------
//  Nilog tests — the source-generated 6/7/8-argument typed overloads: render
//  correctness, structured state, the with/without-exception split, and the
//  disabled-path zero-allocation guarantee that is the whole point of lifting the
//  five-argument ceiling.
//
//  File        : HighArityTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>
/// Covers the generated 6-8 argument typed overloads (<see cref="HighArityOverloadGenerator"/>
/// in Nilog.SourceGenerators), mirroring <see cref="FiveArgTests"/> one arity higher.
/// </summary>
public class HighArityTests
{
    [Fact]
    public void WriteInformation_SixArgs_RendersAndCarriesStructuredProps()
    {
        TestLogger logger = new();

        logger.WriteInformation("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

        Assert.Equal("1 2 3 4 5 6", logger.Single.Message);
        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal(6, logger.Single["F"]);
        Assert.Equal("{A} {B} {C} {D} {E} {F}", logger.Single["{OriginalFormat}"]);
    }

    [Fact]
    public void WriteWarning_SevenArgs_Renders()
    {
        TestLogger logger = new();

        logger.WriteWarning("{A} {B} {C} {D} {E} {F} {G}", 1, 2, 3, 4, 5, 6, 7);

        Assert.Equal("1 2 3 4 5 6 7", logger.Single.Message);
        Assert.Equal(7, logger.Single["G"]);
    }

    [Fact]
    public void WriteDebug_EightArgs_Renders()
    {
        TestLogger logger = new();

        logger.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H}", "a", "b", "c", "d", "e", "f", "g", "h");

        Assert.Equal("a b c d e f g h", logger.Single.Message);
        Assert.Equal("h", logger.Single["H"]);
    }

    [Fact]
    public void Log_EightTypedArgs_RendersAllValues()
    {
        TestLogger logger = new();

        Nilogger.Log(logger, LogLevel.Information, "{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

        Assert.Equal("1 2 3 4 5 6 7 8", logger.Single.Message);
        Assert.Equal(8, logger.Single["H"]);
        Assert.True(logger.Single.HasKey("{OriginalFormat}"));
    }

    [Fact]
    public void WriteError_SixArgsWithException_AttachesExceptionAndRenders()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("boom");

        logger.WriteError("{A} {B} {C} {D} {E} {F}", ex, 1, 2, 3, 4, 5, 6);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("1 2 3 4 5 6", logger.Single.Message);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void WriteCritical_SixArgs_NoException_Renders()
    {
        TestLogger logger = new();

        logger.WriteCritical("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

        Assert.Null(logger.Single.Exception);
        Assert.Equal("1 2 3 4 5 6", logger.Single.Message);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    [Fact]
    public void WriteError_SixArgs_WithLeadingException_StillBindsToExceptionOverload()
    {
        // Same regression guard as the 5-arg case: the no-exception generic overload
        // carries [OverloadResolutionPriority(-1)] so a leading Exception value binds to
        // the dedicated with-exception overload instead of being logged as a plain value.
        TestLogger logger = new();
        InvalidOperationException ex = new("boom");

        logger.WriteError("a {0} {1} {2} {3} {4}", ex, 1, 2, 3, 4, 5);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("a 1 2 3 4 5", logger.Single.Message);
    }

    [Fact]
    public void Log_SixArgs_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        Nilogger.Log(logger, LogLevel.Debug, "{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void DisabledPath_SixTypedArgs_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        for (int i = 0; i < 50; i++)
            logger.WriteDebug("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
            logger.WriteDebug("{A} {B} {C} {D} {E} {F}", 1, 2, 3, 4, 5, 6);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void DisabledPath_EightTypedArgs_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };

        for (int i = 0; i < 50; i++)
            logger.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
            logger.WriteDebug("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, allocated);
    }
}
