// -----------------------------------------------------------------------------
//  Nilog tests — proves Nilog works with ANY logging engine/tool by running it
//  through the real Microsoft.Extensions.Logging pipeline (LoggerFactory +
//  ILoggerProvider). That ILoggerProvider/ILogger contract is exactly how every
//  third-party engine integrates — Serilog, NLog, OpenTelemetry, Seq, Application
//  Insights, AWS, GCP — so if the rendered message and structured state survive
//  this pipeline intact, they survive any MEL-compatible sink.
//
//  File        : LoggingEngineInteropTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>
/// End-to-end interop tests: Nilog → real <see cref="LoggerFactory"/> → a custom
/// <see cref="ILoggerProvider"/> standing in for any third-party logging engine.
/// </summary>
public class LoggingEngineInteropTests
{
    [Fact]
    public void StructuredState_FlowsThroughRealLoggerFactory()
    {
        var provider = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(provider);
        });
        ILogger logger = factory.CreateLogger("Interop");

        logger.WriteInformation("User {UserId} signed in from {Ip}", 42, "10.0.0.1");

        CaptureLogger sink = provider.Logger;

        // 1) The rendered message arrives exactly as any sink would print it.
        Assert.Equal("User 42 signed in from 10.0.0.1", sink.Message);

        // 2) The {OriginalFormat} convention every structured engine reads to recover the
        //    un-rendered template is present and correct.
        Assert.Contains(sink.State, kv =>
            kv.Key == "{OriginalFormat}" && (string?)kv.Value == "User {UserId} signed in from {Ip}");

        // 3) Named placeholders arrive as structured properties (what Serilog/Seq/OTel index).
        Assert.Contains(sink.State, kv => kv.Key == "UserId" && Equals(kv.Value, 42));
        Assert.Contains(sink.State, kv => kv.Key == "Ip" && Equals(kv.Value, "10.0.0.1"));
    }

    [Fact]
    public void HighArity_FlowsThroughRealLoggerFactory()
    {
        var provider = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(provider);
        });
        ILogger logger = factory.CreateLogger("Interop");

        logger.WriteInformation("{A} {B} {C} {D} {E} {F} {G} {H}", 1, 2, 3, 4, 5, 6, 7, 8);

        CaptureLogger sink = provider.Logger;
        Assert.Equal("1 2 3 4 5 6 7 8", sink.Message);
        Assert.Contains(sink.State, kv => kv.Key == "A" && Equals(kv.Value, 1));
        Assert.Contains(sink.State, kv => kv.Key == "H" && Equals(kv.Value, 8));
        Assert.Contains(sink.State, kv => kv.Key == "{OriginalFormat}");
    }

    [Fact]
    public void Exception_FlowsThroughRealLoggerFactory()
    {
        var provider = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(provider);
        });
        ILogger logger = factory.CreateLogger("Interop");
        var ex = new InvalidOperationException("boom");

        logger.WriteError("Checkout failed for cart {CartId}", ex, "CART-7");

        CaptureLogger sink = provider.Logger;
        Assert.Equal(LogLevel.Error, sink.Level);
        Assert.Same(ex, sink.Exception);
        Assert.Equal("Checkout failed for cart CART-7", sink.Message);
        Assert.Contains(sink.State, kv => kv.Key == "CartId" && Equals(kv.Value, "CART-7"));
    }

    [Fact]
    public void DisabledLevel_AtFactory_EmitsNothing()
    {
        var provider = new CaptureProvider();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Warning); // the engine/host decides the level
            b.AddProvider(provider);
        });
        ILogger logger = factory.CreateLogger("Interop");

        logger.WriteInformation("User {UserId} did {Action}", 42, "noop");

        Assert.Null(provider.Logger.Message); // filtered before the sink, zero work done
    }

    // ---- A minimal real ILoggerProvider, the way any engine plugs into MEL ----

    private sealed class CaptureProvider : ILoggerProvider
    {
        public CaptureLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void Dispose() { }
    }

    private sealed class CaptureLogger : ILogger
    {
        public string? Message { get; private set; }
        public LogLevel Level { get; private set; }
        public Exception? Exception { get; private set; }
        public List<KeyValuePair<string, object?>> State { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Exception = exception;
            Message = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
            {
                foreach (KeyValuePair<string, object> kv in kvps)
                {
                    State.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
