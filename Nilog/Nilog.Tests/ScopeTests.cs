// -----------------------------------------------------------------------------
//  Nilog tests — covers WriteScope for single pairs and dictionaries, including
//  copy-safety and the empty/no-op scope path.
//
//  File        : ScopeTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
namespace Nilog.Tests;

/// <summary>Covers <c>WriteScope</c> for single pairs and dictionaries.</summary>
public class ScopeTests
{
    [Fact]
    public void SingleScope_PushesOnePair()
    {
        TestLogger logger = new();

        using (logger.WriteScope("RequestId", "abc-123"))
        {
            logger.WriteInformation("inside scope");
        }

        IReadOnlyList<KeyValuePair<string, object>> values = TestLogger.ScopeValues(logger.Scopes[0]);
        _ = Assert.Single(values);
        Assert.Equal("RequestId", values[0].Key);
        Assert.Equal("abc-123", values[0].Value);
        Assert.Equal("RequestId=abc-123", logger.Scopes[0]!.ToString());
    }

    [Fact]
    public void SingleScope_NullValue_BecomesNA()
    {
        TestLogger logger = new();

        using (logger.WriteScope("Key", null!))
        {
        }

        Assert.Equal("N/A", TestLogger.ScopeValues(logger.Scopes[0])[0].Value);
    }

    [Fact]
    public void SingleScope_Dispose_IsHonored()
    {
        TestLogger logger = new();

        IDisposable scope = logger.WriteScope("K", "V");
        scope.Dispose();

        Assert.Equal(1, logger.ScopeDisposeCount);
    }

    [Fact]
    public void DictionaryScope_Small_PushesAllPairs()
    {
        TestLogger logger = new();
        Dictionary<string, object> ctx = new()
        {
            ["User"] = "alice",
            ["Tenant"] = 7,
        };

        using (logger.WriteScope(ctx))
        {
        }

        IReadOnlyList<KeyValuePair<string, object>> values = TestLogger.ScopeValues(logger.Scopes[0]);
        Assert.Equal(2, values.Count);
        Assert.Contains(values, kv => kv.Key == "User" && (string)kv.Value == "alice");
        Assert.Contains(values, kv => kv.Key == "Tenant" && (int)kv.Value == 7);
    }

    [Fact]
    public void DictionaryScope_Large_PushesAllPairs()
    {
        TestLogger logger = new();
        Dictionary<string, object> ctx = [];
        for (int i = 0; i < 10; i++)
        {
            ctx[$"K{i}"] = i;
        }

        using (logger.WriteScope(ctx))
        {
        }

        IReadOnlyList<KeyValuePair<string, object>> values = TestLogger.ScopeValues(logger.Scopes[0]);
        Assert.Equal(10, values.Count);
    }

    [Fact]
    public void DictionaryScope_NullValue_BecomesNA()
    {
        TestLogger logger = new();
        Dictionary<string, object> ctx = new()
        { ["K"] = null! };

        using (logger.WriteScope(ctx))
        {
        }

        Assert.Equal("N/A", TestLogger.ScopeValues(logger.Scopes[0])[0].Value);
    }

    [Fact]
    public void DictionaryScope_Empty_ReturnsNoOpScope_AndPushesNothing()
    {
        TestLogger logger = new();

        IDisposable scope = logger.WriteScope(new Dictionary<string, object>());
        scope.Dispose();

        Assert.NotNull(scope);
        Assert.Empty(logger.Scopes);
    }

    [Fact]
    public void DictionaryScope_Copies_SoLaterMutationDoesNotLeak()
    {
        TestLogger logger = new();
        Dictionary<string, object> ctx = new()
        { ["K"] = "original" };

        using (logger.WriteScope(ctx))
        {
            ctx["K"] = "mutated";
        }

        Assert.Equal("original", TestLogger.ScopeValues(logger.Scopes[0])[0].Value);
    }

    // -------------------------------------------------------------------------
    // Feature A: WriteScope(IEnumerable<KeyValuePair<string, object>>) overload
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadOnlyDictScope_Small_PushesAllPairs()
    {
        TestLogger logger = new();
        IReadOnlyDictionary<string, object> ctx = new Dictionary<string, object>
        {
            ["User"] = "alice",
            ["Region"] = "eu-west-1",
        };

        using (logger.WriteScope(ctx))
        {
        }

        IReadOnlyList<KeyValuePair<string, object>> values = TestLogger.ScopeValues(logger.Scopes[0]);
        Assert.Equal(2, values.Count);
        Assert.Contains(values, kv => kv.Key == "User" && (string)kv.Value == "alice");
        Assert.Contains(values, kv => kv.Key == "Region" && (string)kv.Value == "eu-west-1");
    }

    [Fact]
    public void ReadOnlyDictScope_Large_PushesAllPairs()
    {
        TestLogger logger = new();
        Dictionary<string, object> backing = [];
        for (int i = 0; i < 8; i++)
        {
            backing[$"K{i}"] = i;
        }
        IReadOnlyDictionary<string, object> ctx = backing;

        using (logger.WriteScope(ctx))
        {
        }

        Assert.Equal(8, TestLogger.ScopeValues(logger.Scopes[0]).Count);
    }

    [Fact]
    public void ReadOnlyDictScope_NullValue_BecomesNA()
    {
        TestLogger logger = new();
        IReadOnlyDictionary<string, object> ctx = new Dictionary<string, object> { ["K"] = null! };

        using (logger.WriteScope(ctx))
        {
        }

        Assert.Equal("N/A", TestLogger.ScopeValues(logger.Scopes[0])[0].Value);
    }

    [Fact]
    public void ReadOnlyDictScope_Empty_ReturnsNoOpScope_AndPushesNothing()
    {
        TestLogger logger = new();
        IReadOnlyDictionary<string, object> ctx = new Dictionary<string, object>();

        IDisposable scope = logger.WriteScope(ctx);
        scope.Dispose();

        Assert.NotNull(scope);
        Assert.Empty(logger.Scopes);
    }

    [Fact]
    public void ReadOnlyDictScope_Copies_SoLaterMutationDoesNotLeak()
    {
        TestLogger logger = new();
        Dictionary<string, object> backing = new() { ["K"] = "original" };
        IReadOnlyDictionary<string, object> ctx = backing;

        using (logger.WriteScope(ctx))
        {
            backing["K"] = "mutated";
        }

        Assert.Equal("original", TestLogger.ScopeValues(logger.Scopes[0])[0].Value);
    }

    [Fact]
    public void EnumerableScope_BindsToEnumerableOverload_WhenTypedAsReadOnly()
    {
        TestLogger logger = new();
        IReadOnlyDictionary<string, object> ctx = new Dictionary<string, object>
        { ["Env"] = "prod" };

        // The variable is typed as IReadOnlyDictionary, so the IEnumerable<KVP> overload
        // wins (IDictionary is not in the IReadOnlyDictionary hierarchy).
        using (logger.WriteScope(ctx))
        {
        }

        IReadOnlyList<KeyValuePair<string, object>> values = TestLogger.ScopeValues(logger.Scopes[0]);
        Assert.Single(values);
        Assert.Equal("Env", values[0].Key);
        Assert.Equal("prod", values[0].Value);
    }
}
