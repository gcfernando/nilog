// -----------------------------------------------------------------------------
//  Nilog tests — covers the async/extensibility hooks (AsyncSinkFilter,
//  UseAsyncSinkProvider, FlushAsync) and the timestamp-timer shutdown.
//
//  File        : HookTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers the async/extensibility hooks and the timestamp-timer shutdown.</summary>
public class HookTests
{
    [Fact]
    public void AsyncSinkFilter_DefaultsToKeepEverything()
    {
        Assert.True(Nilogger.AsyncSinkFilter(LogLevel.Information, "msg", null!));
    }

    [Fact]
    public void UseAsyncSinkProvider_ReplacesFilter_ThenRestore()
    {
        Func<LogLevel, string, Exception, bool> original = Nilogger.AsyncSinkFilter;
        try
        {
            Nilogger.UseAsyncSinkProvider((level, _, _) => level >= LogLevel.Warning);

            Assert.False(Nilogger.AsyncSinkFilter(LogLevel.Information, "m", null!));
            Assert.True(Nilogger.AsyncSinkFilter(LogLevel.Error, "m", null!));
        }
        finally
        {
            Nilogger.UseAsyncSinkProvider(original);
        }
    }

    [Fact]
    public void UseAsyncSinkProvider_Null_LeavesFilterUnchanged()
    {
        Func<LogLevel, string, Exception, bool> before = Nilogger.AsyncSinkFilter;

        Nilogger.UseAsyncSinkProvider(null!);

        Assert.Same(before, Nilogger.AsyncSinkFilter);
    }

    [Fact]
    public async Task FlushAsync_Completes()
    {
        await Nilogger.FlushAsync();
    }

    [Fact]
    public async Task FlushAsync_WithCancelledToken_DoesNotThrow()
    {
        await Nilogger.FlushAsync(new CancellationToken(canceled: true));
    }

    [Fact]
    public void ShutdownUtcTimer_IsIdempotent()
    {
        // Safe to call repeatedly: subsequent calls hit the already-disposed path and
        // are swallowed. Exception formatting keeps working off the last cached value.
        Nilogger.ShutdownUtcTimer();
        Nilogger.ShutdownUtcTimer();
    }
}
