using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

// Closes coverage gaps found by the v1.0.6 coverage run: arities 10-16 on every level,
// scope wrappers, and the fallback scope used when the provider returns a null scope.
public sealed class CoverageGapTests
{
    private sealed class NullScopeLogger : ILogger
    {
        public object? LastScope;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            LastScope = state;
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private static string Template(int n)
    {
        string t = "";
        for (int i = 0; i < n; i++)
        {
            t += "{P" + i + "} ";
        }

        return t.TrimEnd();
    }

    [Fact]
    public void Arity10To16_Information_RendersAndExposesState()
    {
        for (int n = 10; n <= 16; n++)
        {
            TestLogger l = new();
            switch (n)
            {
                case 10: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9); break;
                case 11: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10); break;
                case 12: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11); break;
                case 13: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12); break;
                case 14: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13); break;
                case 15: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14); break;
                default: l.WriteInformation(Template(n), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15); break;
            }

            TestLogger.Entry e = l.Single;
            Assert.Equal(string.Join(" ", Enumerable.Range(0, n)), e.Message);
            Assert.Equal(n - 1, e["P" + (n - 1)]);
        }
    }

    [Fact]
    public void Arity12_AllLevels_RenderAndKeepLevel()
    {
        string t = Template(12);
        TestLogger l = new();
        l.WriteTrace(t, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        l.WriteDebug(t, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        l.WriteWarning(t, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        l.WriteError(t, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        l.WriteCritical(t, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        LogLevel[] expected = [LogLevel.Trace, LogLevel.Debug, LogLevel.Warning, LogLevel.Error, LogLevel.Critical];
        Assert.Equal(expected, l.Entries.Select(x => x.Level).ToArray());
        Assert.All(l.Entries, x => Assert.Equal("0 1 2 3 4 5 6 7 8 9 10 11", x.Message));
    }

    [Fact]
    public void Arity12_Disabled_WritesNothing()
    {
        TestLogger l = new() { Enabled = false };
        l.WriteInformation(Template(12), 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        Assert.Empty(l.Entries);
    }

    [Fact]
    public void Scope_NullProviderScope_ReturnsDisposableFallback()
    {
        NullScopeLogger l = new();
        IDisposable s1 = l.WriteScope("A", 1);
        s1.Dispose();
        s1.Dispose();
        Assert.NotNull(s1);

        for (int n = 2; n <= 6; n++)
        {
            Dictionary<string, object> d = [];
            for (int i = 0; i < n; i++)
            {
                d["K" + i] = i;
            }

            using IDisposable s = l.WriteScope(d);
            IReadOnlyList<KeyValuePair<string, object>> view = (IReadOnlyList<KeyValuePair<string, object>>)l.LastScope!;
            Assert.Equal(n, view.Count);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal("K" + i, view[i].Key);
                Assert.Equal(i, view[i].Value);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => view[n]);
            Assert.NotNull(view.ToString());
        }
    }

    [Fact]
    public void Scope_Enumerates_ViaIEnumerableAndEnumerator()
    {
        TestLogger l = new();
        Dictionary<string, object> d = new() { ["A"] = 1, ["B"] = 2, ["C"] = 3, ["D"] = 4, ["E"] = 5 };
        using (l.WriteScope(d))
        {
        }

        IReadOnlyList<KeyValuePair<string, object>> v = TestLogger.ScopeValues(l.Scopes[0]);
        Assert.Equal(5, v.Count);
        Assert.Equal(5, v.Count(_ => true));
        System.Collections.IEnumerable nonGeneric = (System.Collections.IEnumerable)v;
        int c = 0;
        foreach (object _ in nonGeneric)
        {
            c++;
        }

        Assert.Equal(5, c);
    }
}
