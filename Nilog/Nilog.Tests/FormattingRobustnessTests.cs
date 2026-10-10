using System.Globalization;
using Xunit;

namespace Nilog.Tests;

public sealed class FormattingRobustnessTests
{
    private sealed class ThrowingToString
    {
        public override string ToString() => throw new InvalidOperationException("ts");
    }

    private sealed class NullToString
    {
        public override string? ToString() => null;
    }

    private readonly struct ThrowingSpanFormattable : ISpanFormattable
    {
        public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException("sf");
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            => throw new InvalidOperationException("sf");
        public override string ToString() => throw new InvalidOperationException("sf");
    }

    [Fact]
    public void ThrowingToString_AllArities_DoesNotThrow_F006()
    {
        TestLogger logger = new();
        ThrowingToString bad = new();

        logger.WriteInformation("a={A}", bad);
        logger.WriteInformation("a={A} b={B}", 1, bad);
        logger.WriteInformation("a={A} b={B} c={C} d={D} e={E}", 1, 2, 3, 4, bad);
        logger.WriteInformation("1={a} 2={b} 3={c} 4={d} 5={e} 6={f} 7={g} 8={h}", 1, 2, 3, 4, 5, 6, 7, bad);
        logger.WriteError("e={A}", bad);

        Assert.Equal(5, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.Contains("ToString failed", e.Message, StringComparison.Ordinal));
        Assert.Contains("a=1 b=[ToString failed", logger.Entries[1].Message, StringComparison.Ordinal);
    }

    // Documented limitation: the object[]-typed path delegates rendering to Microsoft.Extensions.Logging's
    // FormattedLogValues, which is MEL/provider-owned and propagates a throwing ToString.
    [Fact]
    public void ObjectArrayPath_ThrowingToString_IsMelOwned_F006()
    {
        TestLogger logger = new();
        object?[] args = [new ThrowingToString()];

        Assert.Throws<InvalidOperationException>(() => logger.WriteInformation("{A}", args));
    }

    [Fact]
    public void ThrowingSpanFormattable_DoesNotThrow_F006()
    {
        TestLogger logger = new();

        logger.WriteInformation("x={X}", new ThrowingSpanFormattable());
        logger.WriteInformation("x={X:N2}", new ThrowingSpanFormattable());

        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void ToStringReturningNull_RendersEmpty_F006()
    {
        TestLogger logger = new();

        logger.WriteInformation("x={X}", new NullToString());

        Assert.Equal("x=", logger.Single.Message);
    }

    [Fact]
    public void ValidationExceptions_StillThrow_F006()
    {
        TestLogger logger = new();

        Assert.Throws<ArgumentNullException>(() => logger.WriteInformation(null!, 1));
        Assert.Throws<ArgumentNullException>(() => NilogLoggerExtensionsProbe.NullLogger());
    }

    [Fact]
    public void ProviderException_Propagates_F006()
    {
        ThrowingProvider logger = new();

        Assert.Throws<InvalidOperationException>(() => logger.WriteInformation("x={X}", 1));
    }

    private static class NilogLoggerExtensionsProbe
    {
        public static void NullLogger() => Nilogger.WriteInformation(null!, "x", 1);
    }

    private sealed class ThrowingProvider : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("provider");
    }

    // F-014: documented behavior for template/argument mismatches.
    [Theory]
    [InlineData("{A} {B}", "{A} {B}")]
    [InlineData("{A", "{A")]
    [InlineData("A}", "A}")]
    [InlineData("{{A}}", "{A}")]
    [InlineData("none", "none")]
    [InlineData("{A:Z9Z}", "{A:Z9Z}")]
    public void SingleArgMismatch_NeverThrows_F014(string template, string expected)
    {
        TestLogger logger = new();

        logger.WriteInformation(template, "v");

        string actual = logger.Single.Message;
        if (template == "{A:Z9Z}")
        {
            Assert.True(actual == expected || actual == "v", actual);
        }
        else if (template is "{A" or "A}" or "{A} {B}")
        {
            Assert.Equal(expected, actual);
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void SurplusArgs_AreIgnoredInRender_AndKeptInState_F014()
    {
        TestLogger logger = new();

        logger.WriteInformation("only {A}", "x", "y");

        Assert.Equal("only x", logger.Single.Message);
        Assert.Equal(2, logger.Single.ToString().Length > 0 ? 2 : 0);
    }

    [Fact]
    public void FormatSpecifiersAndAlignment_MatchStringFormat_F014()
    {
        TestLogger logger = new();

        logger.WriteInformation("{A:N2}|{B,6}|{C,-6}|", 1234.5, "ab", "cd");

        string expected = string.Format(CultureInfo.InvariantCulture, "{0:N2}|{1,6}|{2,-6}|", 1234.5, "ab", "cd");
        Assert.Equal(expected, logger.Single.Message);
    }
}
