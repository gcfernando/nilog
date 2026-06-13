using Microsoft.Extensions.Logging;

namespace Nilog.Benchmark;

/// <summary>
/// A bare-bones <see cref="ILogger"/> for benchmarking. It does no I/O, but it does call
/// the formatter and consume the result, so the measured cost is the real work each
/// logging approach performs (template rendering, boxing, array allocation) without any
/// console/file noise. <see cref="IsEnabled"/> is fixed at construction so we can measure
/// both the enabled and the disabled hot paths.
/// </summary>
public sealed class BenchLogger(bool enabled) : ILogger
{
    /// <summary>Accumulates work so the JIT cannot eliminate the formatting call.</summary>
    public long Sink;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => enabled;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string rendered = formatter(state, exception);
        Sink += rendered.Length;
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();
        public void Dispose() { }
    }
}
