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
    public async Task FlushAsync_AwaitsRegisteredCallback()
    {
        int flushed = 0;
        Task cb(CancellationToken _)
        { Interlocked.Increment(ref flushed); return Task.CompletedTask; }

        Nilogger.RegisterFlush(cb);
        try
        {
            await Nilogger.FlushAsync();
            Assert.Equal(1, flushed);
        }
        finally
        {
            Assert.True(Nilogger.UnregisterFlush(cb));
        }
    }

    [Fact]
    public async Task FlushAsync_AwaitsAllRegisteredCallbacks_InOrder()
    {
        List<int> order = new();
        async Task a(CancellationToken _)
        { await Task.Yield(); lock (order) { order.Add(1); } }
        async Task b(CancellationToken _)
        { await Task.Yield(); lock (order) { order.Add(2); } }

        Nilogger.RegisterFlush(a);
        Nilogger.RegisterFlush(b);
        try
        {
            await Nilogger.FlushAsync();
            Assert.Equal([1, 2], order);
        }
        finally
        {
            _ = Nilogger.UnregisterFlush(a);
            _ = Nilogger.UnregisterFlush(b);
        }
    }

    [Fact]
    public async Task FlushAsync_OneFaultingCallback_StillRunsTheRest_AndThrowsAggregate()
    {
        int second = 0;
        Task bad(CancellationToken _) => throw new InvalidOperationException("sink down");
        Task good(CancellationToken _)
        { Interlocked.Increment(ref second); return Task.CompletedTask; }

        Nilogger.RegisterFlush(bad);
        Nilogger.RegisterFlush(good);
        try
        {
            AggregateException agg = await Assert.ThrowsAsync<AggregateException>(() => Nilogger.FlushAsync());
            _ = Assert.Single(agg.InnerExceptions);
            Assert.Equal(1, second); // the good sink was still flushed despite the bad one
        }
        finally
        {
            _ = Nilogger.UnregisterFlush(bad);
            _ = Nilogger.UnregisterFlush(good);
        }
    }

    [Fact]
    public async Task FlushAsync_AfterUnregister_IsNoOpAgain()
    {
        int flushed = 0;
        Task cb(CancellationToken _)
        { Interlocked.Increment(ref flushed); return Task.CompletedTask; }

        Nilogger.RegisterFlush(cb);
        Assert.True(Nilogger.UnregisterFlush(cb));

        await Nilogger.FlushAsync();
        Assert.Equal(0, flushed);
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
