// -----------------------------------------------------------------------------
//  Nilog tests — verifies that LogState<T> struct enumerators yield the correct
//  key/value pairs in the correct order, and that ScopeWrapper, SmallScopeWrapper,
//  and SingleScope can all be iterated via foreach correctly.
//
//  Background: the original LogState enumerators used "yield return", which the
//  compiler lowers to a heap-allocated state-machine class on every GetEnumerator()
//  call. They were replaced with hand-written struct enumerators that match the
//  pattern used by SingleScope and SmallScopeWrapper. These tests verify that the
//  replacement produces identical output, including entry count and iteration order.
//
//  File        : EnumeratorTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
namespace Nilog.Tests;

/// <summary>
/// Covers LogState struct enumerator correctness and scope-wrapper iteration paths.
/// </summary>
public class EnumeratorTests
{
    // -------------------------------------------------------------------------
    // LogState<T0> — 2 entries: the named value followed by {OriginalFormat}
    // -------------------------------------------------------------------------

    [Fact]
    public void LogStateT0_EnumeratesExactlyTwoEntries_InCorrectOrder()
    {
        TestLogger logger = new();
        logger.WriteInformation("Item {Name}", "apple");

        IReadOnlyList<KeyValuePair<string, object?>> state = logger.Single.State;
        Assert.Equal(2, state.Count);
        Assert.Equal("Name", state[0].Key);
        Assert.Equal("apple", state[0].Value);
        Assert.Equal("{OriginalFormat}", state[1].Key);
        Assert.Equal("Item {Name}", state[1].Value);
    }

    // -------------------------------------------------------------------------
    // LogState<T0, T1> — 3 entries: two named values followed by {OriginalFormat}
    // -------------------------------------------------------------------------

    [Fact]
    public void LogStateT0T1_EnumeratesExactlyThreeEntries_InCorrectOrder()
    {
        TestLogger logger = new();
        logger.WriteDebug("A={A} B={B}", 1, "two");

        IReadOnlyList<KeyValuePair<string, object?>> state = logger.Single.State;
        Assert.Equal(3, state.Count);
        Assert.Equal("A", state[0].Key);
        Assert.Equal(1, state[0].Value);
        Assert.Equal("B", state[1].Key);
        Assert.Equal("two", state[1].Value);
        Assert.Equal("{OriginalFormat}", state[2].Key);
        Assert.Equal("A={A} B={B}", state[2].Value);
    }

    // -------------------------------------------------------------------------
    // LogState<T0, T1, T2> — 4 entries: three named values + {OriginalFormat}
    // -------------------------------------------------------------------------

    [Fact]
    public void LogStateT0T1T2_EnumeratesExactlyFourEntries_InCorrectOrder()
    {
        TestLogger logger = new();
        logger.WriteWarning("{X} {Y} {Z}", 10, 20, 30);

        IReadOnlyList<KeyValuePair<string, object?>> state = logger.Single.State;
        Assert.Equal(4, state.Count);
        Assert.Equal("X", state[0].Key);
        Assert.Equal(10, state[0].Value);
        Assert.Equal("Y", state[1].Key);
        Assert.Equal(20, state[1].Value);
        Assert.Equal("Z", state[2].Key);
        Assert.Equal(30, state[2].Value);
        Assert.Equal("{OriginalFormat}", state[3].Key);
        Assert.Equal("{X} {Y} {Z}", state[3].Value);
    }

    // -------------------------------------------------------------------------
    // SingleScope — struct enumerator produces the one pair
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleScope_CanBeEnumerated_ViaForeach_ProducesOnePair()
    {
        TestLogger logger = new();
        using (logger.WriteScope("Env", "production")) { }

        IEnumerable<KeyValuePair<string, object>> scope =
            (IEnumerable<KeyValuePair<string, object>>)logger.Scopes[0]!;

        List<KeyValuePair<string, object>> collected = [.. scope];
        Assert.Single(collected);
        Assert.Equal("Env", collected[0].Key);
        Assert.Equal("production", collected[0].Value);
    }

    // -------------------------------------------------------------------------
    // SmallScopeWrapper (≤ 4 entries) — struct enumerator yields all items
    // -------------------------------------------------------------------------

    [Fact]
    public void SmallScopeWrapper_CanBeEnumerated_ViaForeach_AllItemsPresent()
    {
        TestLogger logger = new();
        Dictionary<string, object> ctx = new()
        {
            ["A"] = 1,
            ["B"] = 2,
            ["C"] = 3,
        };

        using (logger.WriteScope(ctx)) { }

        IEnumerable<KeyValuePair<string, object>> scope =
            (IEnumerable<KeyValuePair<string, object>>)logger.Scopes[0]!;

        List<KeyValuePair<string, object>> collected = [.. scope];
        Assert.Equal(3, collected.Count);
        Assert.Contains(collected, kv => kv.Key == "A" && Equals(kv.Value, 1));
        Assert.Contains(collected, kv => kv.Key == "B" && Equals(kv.Value, 2));
        Assert.Contains(collected, kv => kv.Key == "C" && Equals(kv.Value, 3));
    }

    // -------------------------------------------------------------------------
    // ScopeWrapper (> 4 entries) — GetEnumerator now returns List<T>.Enumerator
    // directly so duck-typed foreach is allocation-free; correctness is unchanged.
    // -------------------------------------------------------------------------

    [Fact]
    public void ScopeWrapper_CanBeEnumerated_ViaForeach_AllItemsPresent()
    {
        TestLogger logger = new();
        Dictionary<string, object> ctx = [];
        for (int i = 0; i < 8; i++)
        {
            ctx[$"K{i}"] = i;
        }

        using (logger.WriteScope(ctx)) { }

        IEnumerable<KeyValuePair<string, object>> scope =
            (IEnumerable<KeyValuePair<string, object>>)logger.Scopes[0]!;

        List<KeyValuePair<string, object>> collected = [.. scope];
        Assert.Equal(8, collected.Count);
        for (int i = 0; i < 8; i++)
        {
            Assert.Contains(collected, kv => kv.Key == $"K{i}" && Equals(kv.Value, i));
        }
    }

}
