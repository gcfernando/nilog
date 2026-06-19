// -----------------------------------------------------------------------------
//  Nilog tests — the consolidated disabled-path allocation gate. Together with the
//  per-arity DisabledPath_*_AllocatesZeroBytes tests (4/5/6/8 args), these assert
//  exactly 0 bytes allocated across the whole 1–8 typed range and the static
//  Nilogger.Log path. The CI workflow runs these in Release so any change that
//  reintroduces a per-call array/boxing on the disabled path turns the build red.
//
//  File        : AllocationGateTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

public class AllocationGateTests
{
    private static long Measure(Action call)
    {
        for (int i = 0; i < 50; i++) call(); // JIT warmup
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) call();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void DisabledPath_OneTypedArg_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };
        Assert.Equal(0L, Measure(() => logger.WriteDebug("User {Id}", 42)));
    }

    [Fact]
    public void DisabledPath_TwoTypedArgs_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };
        Assert.Equal(0L, Measure(() => logger.WriteDebug("Order {Id} total {Amount}", 42, 99.95m)));
    }

    [Fact]
    public void DisabledPath_ThreeTypedArgs_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };
        Assert.Equal(0L, Measure(() => logger.WriteDebug("{A} {B} {C}", 1, 2, 3)));
    }

    [Fact]
    public void DisabledPath_StaticLog_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Warning };
        Assert.Equal(0L, Measure(() => Nilogger.Log(logger, LogLevel.Debug, "{A} {B} {C} {D} {E}", 1, 2, 3, 4, 5)));
    }

    [Fact]
    public void DisabledPath_WriteErrorTypedNoException_AllocatesZeroBytes()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Critical };
        Assert.Equal(0L, Measure(() => logger.WriteError("Failed {Id} on {Host}", 7, "node-1")));
    }
}
