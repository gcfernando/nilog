// -----------------------------------------------------------------------------
//  Nilog tests — F-007 regression: FlushAsync cancellation / aggregation contract.
//
//  File        : FlushSemanticsTests.cs
// -----------------------------------------------------------------------------
namespace Nilog.Tests;

public class FlushSemanticsTests
{
    private static async Task<T> With<T>(Func<List<Func<CancellationToken, Task>>, Task<T>> body)
    {
        List<Func<CancellationToken, Task>> regs = [];
        try { return await body(regs); }
        finally { foreach (var r in regs) { Nilogger.UnregisterFlush(r); } }
    }

    private static Func<CancellationToken, Task> Reg(List<Func<CancellationToken, Task>> regs, Func<CancellationToken, Task> f)
    {
        Nilogger.RegisterFlush(f);
        regs.Add(f);
        return f;
    }

    [Fact]
    public async Task FailuresBeforeCancellation_ArePreserved()
    {
        using CancellationTokenSource cts = new();
        AggregateException ex = await With(async regs =>
        {
            Reg(regs, _ => throw new InvalidOperationException("first"));
            Reg(regs, _ => { cts.Cancel(); return Task.CompletedTask; });
            bool third = false;
            Reg(regs, _ => { third = true; return Task.CompletedTask; });
            AggregateException e = await Assert.ThrowsAsync<AggregateException>(() => Nilogger.FlushAsync(cts.Token));
            Assert.False(third, "callbacks after cancellation must not start");
            return e;
        });
        Assert.Contains(ex.InnerExceptions, e => e is InvalidOperationException { Message: "first" });
        Assert.Contains(ex.InnerExceptions, e => e is OperationCanceledException);
    }

    [Fact]
    public async Task CancellationWithoutFailures_ThrowsCanceled()
    {
        using CancellationTokenSource cts = new();
        await With(async regs =>
        {
            Reg(regs, _ => { cts.Cancel(); return Task.CompletedTask; });
            Reg(regs, _ => Task.CompletedTask);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Nilogger.FlushAsync(cts.Token));
            return 0;
        });
    }

    [Fact]
    public async Task CallbackOwnCancellation_DoesNotStopLaterCallbacks()
    {
        bool second = false;
        AggregateException e = await With(async regs =>
        {
            Reg(regs, _ => throw new OperationCanceledException("internal timeout"));
            Reg(regs, _ => { second = true; return Task.CompletedTask; });
            return await Assert.ThrowsAsync<AggregateException>(() => Nilogger.FlushAsync());
        });
        Assert.True(second);
        Assert.Single(e.InnerExceptions);
    }

    [Fact]
    public async Task CanceledTaskFromCallback_DoesNotAbortRemaining()
    {
        bool second = false;
        await With(async regs =>
        {
            Reg(regs, _ => Task.FromCanceled(new CancellationToken(true)));
            Reg(regs, _ => { second = true; return Task.CompletedTask; });
            await Assert.ThrowsAsync<AggregateException>(() => Nilogger.FlushAsync());
            return 0;
        });
        Assert.True(second);
    }

    [Fact]
    public async Task NonCooperativeCallback_IsAbandonedOnCancel_AndLaterFaultIsObserved()
    {
        TaskCompletionSource hang = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));
        DateTime start = DateTime.UtcNow;
        await With(async regs =>
        {
            Reg(regs, _ => hang.Task);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Nilogger.FlushAsync(cts.Token));
            return 0;
        });
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(5));
        bool unobserved = false;
        EventHandler<UnobservedTaskExceptionEventArgs> h = (_, _) => unobserved = true;
        TaskScheduler.UnobservedTaskException += h;
        try
        {
            hang.SetException(new InvalidOperationException("late"));
            hang = null!;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(unobserved);
        }
        finally { TaskScheduler.UnobservedTaskException -= h; }
    }

    [Fact]
    public async Task NullTask_SyncThrow_Fault_AllAttempted_InOrder()
    {
        List<int> order = [];
        AggregateException e = await With(async regs =>
        {
            Reg(regs, _ => { order.Add(1); return null!; });
            Reg(regs, _ => { order.Add(2); throw new InvalidOperationException("sync"); });
            Reg(regs, _ => { order.Add(3); return Task.FromException(new IOException("fault")); });
            Reg(regs, _ => { order.Add(4); return Task.CompletedTask; });
            return await Assert.ThrowsAsync<AggregateException>(() => Nilogger.FlushAsync());
        });
        Assert.Equal([1, 2, 3, 4], order);
        Assert.Equal(2, e.InnerExceptions.Count);
    }

    [Fact]
    public async Task DuplicateRegistration_RunsTwice_UnregisterRemovesOne()
    {
        int n = 0;
        Func<CancellationToken, Task> f = _ => { n++; return Task.CompletedTask; };
        Nilogger.RegisterFlush(f);
        Nilogger.RegisterFlush(f);
        try
        {
            await Nilogger.FlushAsync();
            Assert.Equal(2, n);
            Assert.True(Nilogger.UnregisterFlush(f));
            await Nilogger.FlushAsync();
            Assert.Equal(3, n);
        }
        finally { while (Nilogger.UnregisterFlush(f)) { } }
    }

    [Fact]
    public async Task ConcurrentRegisterUnregisterAndFlush_NoCorruption()
    {
        int hits = 0;
        Func<CancellationToken, Task>[] fs = Enumerable.Range(0, 32).Select(_ => (Func<CancellationToken, Task>)(_ => { Interlocked.Increment(ref hits); return Task.CompletedTask; })).ToArray();
        Task[] workers = Enumerable.Range(0, 8).Select(w => Task.Run(async () =>
        {
            for (int i = 0; i < 500; i++)
            {
                Func<CancellationToken, Task> f = fs[(w * 4 + i) % fs.Length];
                Nilogger.RegisterFlush(f);
                await Nilogger.FlushAsync();
                Nilogger.UnregisterFlush(f);
            }
        })).ToArray();
        await Task.WhenAll(workers);
        foreach (var f in fs) { while (Nilogger.UnregisterFlush(f)) { } }
        Assert.True(hits > 0);
        Assert.True(Nilogger.FlushAsync().IsCompletedSuccessfully);
    }
}
