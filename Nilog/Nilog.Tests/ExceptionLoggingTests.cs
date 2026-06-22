// -----------------------------------------------------------------------------
//  Nilog tests — covers WriteErrorException / WriteCriticalException, the
//  default report formatter, inner/aggregate exceptions, and the pluggable
//  ExceptionFormatter.
//
//  File        : ExceptionLoggingTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers <c>WriteErrorException</c>, <c>WriteCriticalException</c>, and the formatter.</summary>
public class ExceptionLoggingTests
{
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void ErrorException_LogsAtError_WithDefaultFields()
    {
        TestLogger logger = new();
        Exception ex = Thrown("something failed");

        logger.WriteErrorException(ex);

        string msg = logger.Single.Message;
        Assert.Equal(LogLevel.Error, logger.Single.Level);
        // Basic report uses compact single-line format: "[Title] Type: Message (Source=..., HResult=...)"
        Assert.Contains("[System Error]", msg);
        Assert.Contains("System.InvalidOperationException", msg);
        Assert.Contains("something failed", msg);
        Assert.Contains("HResult", msg);
    }

    [Fact]
    public void CriticalException_LogsAtCritical_WithDefaultTitle()
    {
        TestLogger logger = new();
        Exception ex = Thrown("fatal failure");

        logger.WriteCriticalException(ex);

        Assert.Equal(LogLevel.Critical, logger.Single.Level);
        Assert.Contains("[Critical System Error]", logger.Single.Message);
    }

    [Fact]
    public void ErrorException_CustomTitle_IsUsed()
    {
        TestLogger logger = new();

        logger.WriteErrorException(Thrown("x"), title: "Payment Failure");

        Assert.Contains("[Payment Failure]", logger.Single.Message);
    }

    [Fact]
    public void ErrorException_WithoutDetails_OmitsStackTrace()
    {
        TestLogger logger = new();

        logger.WriteErrorException(Thrown("x"), moreDetailsEnabled: false);

        Assert.DoesNotContain("Stack Trace", logger.Single.Message);
    }

    [Fact]
    public void ErrorException_WithDetails_IncludesStackTrace()
    {
        TestLogger logger = new();

        logger.WriteErrorException(Thrown("x"), moreDetailsEnabled: true);

        Assert.Contains("Stack Trace", logger.Single.Message);
    }

    [Fact]
    public void ErrorException_WithDetails_IncludesInnerException()
    {
        TestLogger logger = new();
        Exception ex;
        try
        {
            try
            {
                throw new InvalidOperationException("inner cause");
            }
            catch (Exception inner)
            {
                throw new ApplicationException("outer wrapper", inner);
            }
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        logger.WriteErrorException(ex, moreDetailsEnabled: true);

        Assert.Contains("Inner Exceptions", logger.Single.Message);
        Assert.Contains("inner cause", logger.Single.Message);
    }

    [Fact]
    public void ErrorException_WithDetails_ExpandsAggregateException()
    {
        TestLogger logger = new();
        AggregateException agg = new(
            "aggregate root",
            new InvalidOperationException("first branch"),
            new ArgumentException("second branch"));

        logger.WriteErrorException(agg, moreDetailsEnabled: true);

        string msg = logger.Single.Message;
        Assert.Contains("first branch", msg);
        Assert.Contains("second branch", msg);
    }

    [Fact]
    public void WriteErrorException_AttachesExceptionObjectToEntry()
    {
        TestLogger logger = new();
        Exception ex = Thrown("attached");

        logger.WriteErrorException(ex);

        Assert.Same(ex, logger.Single.Exception);
    }

    [Fact]
    public void WriteCriticalException_AttachesExceptionObjectToEntry()
    {
        TestLogger logger = new();
        Exception ex = Thrown("attached critical");

        logger.WriteCriticalException(ex);

        Assert.Same(ex, logger.Single.Exception);
    }

    [Fact]
    public void ErrorException_BasicReport_IsCompactSingleLine()
    {
        TestLogger logger = new();
        Exception ex = Thrown("disk full");

        logger.WriteErrorException(ex, title: "IO Error", moreDetailsEnabled: false);

        string msg = logger.Single.Message;
        Assert.StartsWith("[IO Error]", msg);
        Assert.DoesNotContain("\n", msg);
        Assert.DoesNotContain("Timestamp", msg);
    }

    [Fact]
    public void ErrorException_DetailedReport_IsVerboseMultiLine()
    {
        TestLogger logger = new();
        Exception ex = Thrown("disk full");

        logger.WriteErrorException(ex, title: "IO Error", moreDetailsEnabled: true);

        string msg = logger.Single.Message;
        Assert.Contains("Timestamp      :", msg);
        Assert.Contains("Title          : IO Error", msg);
        Assert.Contains("Exception Type : System.InvalidOperationException", msg);
        Assert.Contains("Stack Trace", msg);
    }

    [Fact]
    public void ErrorException_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new()
        { MinLevel = LogLevel.Critical };

        logger.WriteErrorException(Thrown("x"));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void CustomExceptionFormatter_IsHonored_ThenResets()
    {
        TestLogger logger = new();
        try
        {
            Nilogger.ExceptionFormatter = (ex, title, more) => $"CUSTOM|{title}|{ex.Message}|{more}";

            logger.WriteErrorException(Thrown("oops"), title: "T", moreDetailsEnabled: true);

            Assert.Equal("CUSTOM|T|oops|True", logger.Single.Message);
        }
        finally
        {
            // Assigning null restores the built-in formatter.
            Nilogger.ExceptionFormatter = null!;
        }

        TestLogger logger2 = new();
        logger2.WriteErrorException(Thrown("again"));
        Assert.Contains("System.InvalidOperationException", logger2.Single.Message);
    }
}
