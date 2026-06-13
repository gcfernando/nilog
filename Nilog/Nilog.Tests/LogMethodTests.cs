using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers the static <c>Nilogger.Log(...)</c> family.</summary>
public class LogMethodTests
{
    [Fact]
    public void Log_PlainMessage_IsCaptured()
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, LogLevel.Information, "hello world");

        Assert.Equal("hello world", logger.Single.Message);
        Assert.Equal(LogLevel.Information, logger.Single.Level);
    }

    [Fact]
    public void Log_NullMessage_BecomesNA()
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, LogLevel.Information, (string)null!);

        Assert.Equal("N/A", logger.Single.Message);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void Log_EmitsRequestedLevel(LogLevel level)
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, level, "msg");

        Assert.Equal(level, logger.Single.Level);
    }

    [Fact]
    public void Log_OneTypedArg_RendersAndCarriesState()
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, LogLevel.Warning, "x = {X}", 9);

        Assert.Equal("x = 9", logger.Single.Message);
        Assert.Equal(9, logger.Single["X"]);
        Assert.True(logger.Single.HasKey("{OriginalFormat}"));
    }

    [Fact]
    public void Log_TwoTypedArgs_Render()
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, LogLevel.Information, "{A}+{B}", 2, 3);

        Assert.Equal("2+3", logger.Single.Message);
        Assert.Equal(2, logger.Single["A"]);
        Assert.Equal(3, logger.Single["B"]);
    }

    [Fact]
    public void Log_ThreeTypedArgs_Render()
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, LogLevel.Information, "{A}-{B}-{C}", 1, 2, 3);

        Assert.Equal("1-2-3", logger.Single.Message);
        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal(2, logger.Single["B"]);
        Assert.Equal(3, logger.Single["C"]);
    }

    [Fact]
    public void Log_ParamsArray_NoException_Renders()
    {
        var logger = new TestLogger();

        // Four arguments: no typed overload matches, so the params object[] path runs.
        Nilogger.Log(logger, LogLevel.Information, "{A} {B} {C} {D}", 1, 2, 3, 4);

        Assert.Equal("1 2 3 4", logger.Single.Message);
        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal(4, logger.Single["D"]);
    }

    [Fact]
    public void Log_ParamsArray_WithException_AttachesException()
    {
        var logger = new TestLogger();
        var ex = new InvalidOperationException("boom");

        Nilogger.Log(logger, LogLevel.Error, "a {0} {1} {2} {3}", ex, 1, 2, 3, 4);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("a 1 2 3 4", logger.Single.Message);
    }

    [Fact]
    public void Log_ExceptionFirstOverload_AttachesException()
    {
        var logger = new TestLogger();
        var ex = new InvalidOperationException("kaboom");

        Nilogger.Log(logger, LogLevel.Critical, ex, "msg {0}", 7);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("msg 7", logger.Single.Message);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    [Fact]
    public void Log_WhenLevelDisabled_DoesNothing()
    {
        var logger = new TestLogger { MinLevel = LogLevel.Warning };

        Nilogger.Log(logger, LogLevel.Information, "should not appear");
        Nilogger.Log(logger, LogLevel.Debug, "{X}", 1);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Log_AllLevels_ShareStableEventIdAcrossArgCounts()
    {
        var logger = new TestLogger();

        Nilogger.Log(logger, LogLevel.Information, "no args");
        Nilogger.Log(logger, LogLevel.Information, "one {A}", 1);
        Nilogger.Log(logger, LogLevel.Information, "two {A}{B}", 1, 2);

        Assert.Equal(logger.Entries[0].EventId, logger.Entries[1].EventId);
        Assert.Equal(logger.Entries[1].EventId, logger.Entries[2].EventId);
    }
}
