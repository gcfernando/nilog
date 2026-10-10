using Xunit;

namespace Nilog.Tests;

public sealed class ExceptionReportTests
{
    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("boom");
        public override string? StackTrace => throw new InvalidOperationException("boom");
        public override string? Source { get => throw new InvalidOperationException("boom"); set { } }
    }

    [Fact]
    public void Compact_FlattensNewlines_F009()
    {
        TestLogger logger = new();

        logger.WriteErrorException(new InvalidOperationException("line1\r\nFAKE LOG LINE\nline3"), "T\nX");

        string msg = logger.Single.Message;
        Assert.DoesNotContain('\n', msg);
        Assert.DoesNotContain('\r', msg);
        Assert.Contains("FAKE LOG LINE", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void HugeMessage_IsBounded_F009()
    {
        TestLogger logger = new();
        Exception ex = new InvalidOperationException(new string('x', 5_000_000));

        logger.WriteErrorException(ex);
        logger.WriteErrorException(ex, "t", moreDetailsEnabled: true);

        Assert.All(logger.Entries, e => Assert.True(e.Message.Length < 40_000));
        Assert.Contains("truncated", logger.Entries[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DepthLimit_IsStatedInReport_F009()
    {
        TestLogger logger = new();
        Exception ex = new("e0");
        for (int i = 1; i <= 8; i++)
        {
            ex = new Exception("e" + i, ex);
        }

        logger.WriteErrorException(ex, "t", moreDetailsEnabled: true);

        Assert.Contains("omitted", logger.Single.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevelAggregate_ListsAllInnerExceptions_F009()
    {
        TestLogger logger = new();
        AggregateException ex = new("agg", new InvalidOperationException("first-inner"), new ArgumentException("second-inner"));

        logger.WriteErrorException(ex, "t", moreDetailsEnabled: true);

        string msg = logger.Single.Message;
        Assert.Contains("first-inner", msg, StringComparison.Ordinal);
        Assert.Contains("second-inner", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void WideAggregate_IsNodeBounded_F009()
    {
        TestLogger logger = new();
        AggregateException ex = new(Enumerable.Range(0, 1000).Select(i => new Exception("n" + i)));

        logger.WriteErrorException(ex, "t", moreDetailsEnabled: true);

        Assert.True(logger.Single.Message.Length < 40_000);
        Assert.Contains("omitted", logger.Single.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostileGetters_DoNotBreakLogging_F009()
    {
        TestLogger logger = new();
        HostileException ex = new();

        logger.WriteErrorException(ex);
        logger.WriteErrorException(ex, "t", moreDetailsEnabled: true);

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("Message unavailable", logger.Entries[0].Message, StringComparison.Ordinal);
        Assert.Same(ex, logger.Entries[0].Exception);
    }

    [Fact]
    public void ThrowingUserFormatter_FallsBackToBuiltIn_F006()
    {
        TestLogger logger = new();
        Func<Exception, string, bool, string> original = Nilogger.ExceptionFormatter;
        try
        {
            Nilogger.ExceptionFormatter = static (_, _, _) => throw new InvalidOperationException("fmt");

            logger.WriteErrorException(new Exception("real"));

            Assert.Contains("ExceptionFormatter failed", logger.Single.Message, StringComparison.Ordinal);
            Assert.Contains("real", logger.Single.Message, StringComparison.Ordinal);
        }
        finally
        {
            Nilogger.ExceptionFormatter = original;
        }
    }

    [Fact]
    public void ExceptionData_IsNotRendered_F009()
    {
        TestLogger logger = new();
        Exception ex = new("m");
        ex.Data["secret"] = "hunter2";

        logger.WriteErrorException(ex, "t", moreDetailsEnabled: true);

        Assert.DoesNotContain("hunter2", logger.Single.Message, StringComparison.Ordinal);
    }
}
