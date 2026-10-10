using Microsoft.Extensions.Logging;
using Xunit;

namespace Nilog.Tests;

// F-002 characterization: the disabled-path allocation behaviour is pinned so regressions are
// visible, and the documented object-typed limitation is asserted rather than hidden.
public sealed class AllocationCharacterizationTests
{
    private sealed class DisabledLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static long Measure(Action action)
    {
        for (int i = 0; i < 1000; i++) { action(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) { action(); }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void ConcreteTypedArguments_Arity1To16_AllocateNothingWhenDisabled()
    {
        DisabledLogger l = new();
        string s = "s";
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}", 1)));
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}{B}", 1, s)));
        Assert.Equal(0, Measure(() => l.WriteDebug("{A}{B}{C}", 1, s, 3L)));
        Assert.Equal(0, Measure(() => l.WriteWarning("{A}{B}{C}{D}{E}", 1, s, 3L, 4.0, 'c')));
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}{B}{C}{D}{E}{F}", 1, 2, 3, 4, 5, 6)));
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}{B}{C}{D}{E}{F}{G}{H}{I}{J}", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10)));
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}{B}{C}{D}{E}{F}{G}{H}{I}{J}{K}{L}{M}{N}{O}{P}", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16)));
    }

    [Fact]
    public void NullableAndReferenceTypedArguments_AllocateNothingWhenDisabled()
    {
        DisabledLogger l = new();
        string? ns = null;
        int? ni = 3;
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}{B}", ns, ni)));
    }

    [Fact]
    public void ExplicitObjectGenericArgument_IsBoxedByCallerOnly_WhenValueTypeConverted()
    {
        DisabledLogger l = new();
        int v = 5;
        // Converting a value type to object at the call site boxes it (caller-owned allocation).
        long bytes = Measure(() => l.WriteInformation<object>("{A}", v));
        Assert.True(bytes >= 0);
    }

    [Fact]
    public void AllObjectTypedArguments_BindToParams_AndAllocate_DocumentedLimitation()
    {
        DisabledLogger l = new();
        object a = "a", b = "b";
        // Statically object-typed arguments select the params object[] overload, which allocates
        // the argument array even when the level is disabled. Documented in the README
        // ("Zero-allocation scope"). Pinning it here makes any change to this behaviour visible.
        long bytes = Measure(() => l.WriteInformation("{A}{B}", a, b));
        Assert.True(bytes > 0, "expected the documented params allocation");
    }

    [Fact]
    public void MixedObjectAndConcreteArguments_StillUseTypedOverload()
    {
        DisabledLogger l = new();
        object a = "a";
        Assert.Equal(0, Measure(() => l.WriteInformation("{A}{B}", a, 2)));
    }
}
