using Microsoft.Extensions.Logging;
using Xunit;

namespace Nilog.Tests;

public sealed class ScopeValidationTests
{
    private sealed class ScopeLogger : ILogger
    {
        public readonly LoggerExternalScopeProvider Provider = new();
        public List<string> Seen { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => Provider.Push(state);
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Provider.ForEachScope((s, l) =>
            {
                if (s is IEnumerable<KeyValuePair<string, object>> kvs)
                {
                    l.Seen.Add(string.Join(",", kvs.Select(k => k.Key + "=" + k.Value)));
                }
            }, this);
        }
    }

    private sealed class LyingCollection(int count, int real) : IReadOnlyCollection<KeyValuePair<string, object>>
    {
        public int Count => count;

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            for (int i = 0; i < real; i++)
            {
                yield return new("k" + i, i);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IEnumerable<KeyValuePair<string, object>> Throwing()
    {
        yield return new("a", 1);
        throw new InvalidOperationException("enum");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void DictionaryOverload_RejectsBadKeys_F010(string key)
    {
        ScopeLogger logger = new();
        Dictionary<string, object> d = new() { ["ok"] = 1, [key] = 2 };

        Assert.Throws<ArgumentException>(() => logger.WriteScope(d));
    }

    [Fact]
    public void EnumerableOverload_RejectsNullAndWhitespaceKeys_F010()
    {
        ScopeLogger logger = new();

        Assert.Throws<ArgumentException>(() => logger.WriteScope(new List<KeyValuePair<string, object>> { new(null!, 1) }));
        Assert.Throws<ArgumentException>(() => logger.WriteScope(new List<KeyValuePair<string, object>> { new(" ", 1) }));
        Assert.Throws<ArgumentException>(() => logger.WriteScope(new List<KeyValuePair<string, object>> { new("a", 1), new("b", 1), new("c", 1), new("d", 1), new("e", 1), new("", 1) }));
    }

    [Fact]
    public void SingleAndTypedOverloads_StillRejectBadKeys_F010()
    {
        ScopeLogger logger = new();

        Assert.Throws<ArgumentException>(() => logger.WriteScope(" ", 1));
        Assert.Throws<ArgumentException>(() => logger.WriteScope("a", 1, "", 2));
    }

    [Fact]
    public void CountSmallerThanEnumeration_NoOverflowOrLoss_F010()
    {
        ScopeLogger logger = new();

        using (logger.WriteScope(new LyingCollection(2, 6)))
        {
            logger.LogInformation("x");
        }

        Assert.Equal("k0=0,k1=1,k2=2,k3=3,k4=4,k5=5", logger.Seen[0]);
    }

    [Fact]
    public void CountLargerThanEnumeration_NoDefaultEntries_F010()
    {
        ScopeLogger logger = new();

        using (logger.WriteScope(new LyingCollection(4, 2)))
        {
            logger.LogInformation("x");
        }

        Assert.Equal("k0=0,k1=1", logger.Seen[0]);
    }

    [Fact]
    public void LyingCountWithZeroEntries_ReturnsNoOpScope_F010()
    {
        ScopeLogger logger = new();

        using (logger.WriteScope(new LyingCollection(3, 0)))
        {
            logger.LogInformation("x");
        }

        Assert.Empty(logger.Seen);
    }

    [Fact]
    public void ThrowingEnumerator_Propagates_AndOpensNoScope_F010()
    {
        ScopeLogger logger = new();

        Assert.Throws<InvalidOperationException>(() => logger.WriteScope(Throwing()));
        logger.LogInformation("x");

        Assert.Empty(logger.Seen);
    }

    [Fact]
    public void NonRepeatableEnumerable_IsEnumeratedOnce_F010()
    {
        ScopeLogger logger = new();
        int enumerations = 0;
        IEnumerable<KeyValuePair<string, object>> Once()
        {
            enumerations++;
            yield return new("a", 1);
            yield return new("b", 2);
        }

        using (logger.WriteScope(Once()))
        {
            logger.LogInformation("x");
        }

        Assert.Equal(1, enumerations);
        Assert.Equal("a=1,b=2", logger.Seen[0]);
    }

    [Fact]
    public void DuplicateKeys_ArePassedThrough_AndNullValuesBecomeNA_F010()
    {
        ScopeLogger logger = new();

        using (logger.WriteScope(new List<KeyValuePair<string, object>> { new("a", 1), new("a", null!) }))
        {
            logger.LogInformation("x");
        }

        Assert.Equal("a=1,a=N/A", logger.Seen[0]);
    }

    [Fact]
    public void NestedScopes_AreDisposedInOrder_F010()
    {
        ScopeLogger logger = new();

        using (logger.WriteScope("outer", 1))
        {
            using (logger.WriteScope(new Dictionary<string, object> { ["inner"] = 2 }))
            {
                logger.LogInformation("both");
            }

            logger.LogInformation("outer-only");
        }

        logger.LogInformation("none");

        Assert.Equal(["outer=1", "inner=2"], logger.Seen.Take(2).OrderByDescending(s => s).ToArray());
        Assert.Equal(3, logger.Seen.Count);
    }

    [Fact]
    public void ConcurrentDictionaryMutation_NeverCorruptsScope_F010()
    {
        ScopeLogger logger = new();
        System.Collections.Concurrent.ConcurrentDictionary<string, object> d = new();
        for (int i = 0; i < 3; i++)
        {
            d["k" + i] = i;
        }

        using CancellationTokenSource cts = new();
        Task mutator = Task.Run(() =>
        {
            int n = 0;
            while (!cts.IsCancellationRequested)
            {
                d["m" + (n++ % 50)] = n;
            }
        });

        for (int i = 0; i < 2000; i++)
        {
            using IDisposable s = logger.WriteScope((IEnumerable<KeyValuePair<string, object>>)d);
        }

        cts.Cancel();
        mutator.Wait();
    }
}
