using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>
/// A minimal in-memory <see cref="ILogger"/> used across the test suite. It records every
/// entry (level, event id, rendered message, exception, and structured state) and every
/// scope that is pushed, so tests can assert on exactly what Nilog handed to the sink.
/// </summary>
internal sealed class TestLogger : ILogger
{
    /// <summary>A single captured log entry.</summary>
    public sealed class Entry
    {
        public LogLevel Level { get; init; }
        public EventId EventId { get; init; }
        public string Message { get; init; } = "";
        public Exception? Exception { get; init; }
        public IReadOnlyList<KeyValuePair<string, object?>> State { get; init; } = [];

        /// <summary>Looks up a structured value by template property name.</summary>
        public object? this[string key]
        {
            get
            {
                foreach (var kv in State)
                {
                    if (kv.Key == key)
                    {
                        return kv.Value;
                    }
                }

                return null;
            }
        }

        public bool HasKey(string key)
        {
            foreach (var kv in State)
            {
                if (kv.Key == key)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public List<Entry> Entries { get; } = [];
    public List<object?> Scopes { get; } = [];
    public int ScopeDisposeCount { get; private set; }

    /// <summary>When false, <see cref="IsEnabled"/> returns false for every level.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The lowest level this logger reports as enabled.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    public Entry Single =>
        Entries.Count == 1 ? Entries[0] : throw new InvalidOperationException($"Expected exactly one entry but found {Entries.Count}.");

    public Entry Last => Entries[^1];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        Scopes.Add(state);
        return new Tracker(this);
    }

    public bool IsEnabled(LogLevel logLevel) =>
        Enabled && logLevel != LogLevel.None && logLevel >= MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var kvps = new List<KeyValuePair<string, object?>>();
        if (state is IEnumerable<KeyValuePair<string, object>> enumerable)
        {
            foreach (var kv in enumerable)
            {
                kvps.Add(new KeyValuePair<string, object?>(kv.Key, kv.Value));
            }
        }

        Entries.Add(new Entry
        {
            Level = logLevel,
            EventId = eventId,
            Message = formatter(state, exception),
            Exception = exception,
            State = kvps
        });
    }

    /// <summary>Exposes the structured payload of a pushed scope for assertions.</summary>
    public static IReadOnlyList<KeyValuePair<string, object>> ScopeValues(object? scope) =>
        (IReadOnlyList<KeyValuePair<string, object>>)scope!;

    private sealed class Tracker(TestLogger owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.ScopeDisposeCount++;
            }
        }
    }
}
