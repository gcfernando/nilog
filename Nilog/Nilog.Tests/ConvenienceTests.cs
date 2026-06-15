// -----------------------------------------------------------------------------
//  Nilog tests — covers the Write* extension methods (typed and params forms)
//  across all six log levels.
//
//  File        : ConvenienceTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers the <c>Write*</c> extension methods, both typed and params forms.</summary>
public class ConvenienceTests
{
    [Fact]
    public void Trace_Plain_LogsAtTrace()
    {
        TestLogger logger = new();
        logger.WriteTrace("trace message");
        Assert.Equal(LogLevel.Trace, logger.Single.Level);
        Assert.Equal("trace message", logger.Single.Message);
    }

    [Fact]
    public void Debug_Plain_LogsAtDebug()
    {
        TestLogger logger = new();
        logger.WriteDebug("debug message");
        Assert.Equal(LogLevel.Debug, logger.Single.Level);
    }

    [Fact]
    public void Information_Plain_LogsAtInformation()
    {
        TestLogger logger = new();
        logger.WriteInformation("info message");
        Assert.Equal(LogLevel.Information, logger.Single.Level);
    }

    [Fact]
    public void Warning_Plain_LogsAtWarning()
    {
        TestLogger logger = new();
        logger.WriteWarning("warn message");
        Assert.Equal(LogLevel.Warning, logger.Single.Level);
    }

    [Fact]
    public void Error_Plain_LogsAtError()
    {
        TestLogger logger = new();
        logger.WriteError("error message");
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void Critical_Plain_LogsAtCritical()
    {
        TestLogger logger = new();
        logger.WriteCritical("critical message");
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    [Fact]
    public void Typed_OneArg_BindsToTypedOverload_NoOriginalFormatLoss()
    {
        TestLogger logger = new();

        logger.WriteInformation("User {Id} signed in", 42);

        Assert.Equal("User 42 signed in", logger.Single.Message);
        Assert.Equal(42, logger.Single["Id"]);
        Assert.Equal("User {Id} signed in", logger.Single["{OriginalFormat}"]);
    }

    [Fact]
    public void Typed_TwoArgs_Render()
    {
        TestLogger logger = new();

        logger.WriteWarning("Order {Id} total {Amount}", 7, 19.95);

        Assert.Equal(7, logger.Single["Id"]);
        Assert.Equal(19.95, logger.Single["Amount"]);
        Assert.Contains("Order 7 total 19.95", logger.Single.Message);
    }

    [Fact]
    public void Typed_ThreeArgs_Render()
    {
        TestLogger logger = new();

        logger.WriteDebug("{A} {B} {C}", 1, "two", 3.0);

        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal("two", logger.Single["B"]);
        Assert.Equal(3.0, logger.Single["C"]);
    }

    [Fact]
    public void Params_FourArgs_UsesParamsPath()
    {
        TestLogger logger = new();

        logger.WriteInformation("{A} {B} {C} {D}", 1, 2, 3, 4);

        Assert.Equal("1 2 3 4", logger.Single.Message);
        Assert.Equal(1, logger.Single["A"]);
        Assert.Equal(4, logger.Single["D"]);
    }

    [Fact]
    public void Error_WithException_Typed_AttachesException()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("bad");

        logger.WriteError("Failed for {Id}", ex, 5);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("Failed for 5", logger.Single.Message);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void Critical_WithException_Typed_AttachesException()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("fatal");

        logger.WriteCritical("Crash for {Id}", ex, 9);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("Crash for 9", logger.Single.Message);
    }

    [Fact]
    public void Error_WithException_Params_AttachesException()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("bad");

        logger.WriteError("vals {0} {1} {2} {3}", ex, 1, 2, 3, 4);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal("vals 1 2 3 4", logger.Single.Message);
    }

    [Theory]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    public void Convenience_WhenDisabled_DoesNothing(LogLevel min)
    {
        TestLogger logger = new()
        { MinLevel = min };

        logger.WriteTrace("t");
        logger.WriteDebug("d");
        logger.WriteInformation("i {X}", 1);

        Assert.DoesNotContain(logger.Entries, e => e.Level < min);
    }

    [Fact]
    public void Convenience_WhenLoggerFullyDisabled_AllocatesNothingObservable()
    {
        TestLogger logger = new()
        { Enabled = false };

        logger.WriteTrace("t");
        logger.WriteInformation("i {X}", 1);
        logger.WriteError("e {X} {Y} {Z} {W}", 1, 2, 3, 4);

        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(2_000)]
    public void Convenience_RepeatedCalls_AllCaptured(int count)
    {
        TestLogger logger = new();

        for (int i = 0; i < count; i++)
        {
            logger.WriteInformation("iteration {N}", i);
        }

        Assert.Equal(count, logger.Entries.Count);
        Assert.Equal(count - 1, logger.Last["N"]);
    }

    // -------------------------------------------------------------------------
    // Feature C: WriteError / WriteCritical no-args-no-params overloads
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteError_NoArgs_WithException_AttachesExceptionAndLogsMessage()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("payment declined");

        logger.WriteError("Payment failed", ex);

        Assert.Equal(LogLevel.Error, logger.Single.Level);
        Assert.Equal("Payment failed", logger.Single.Message);
        Assert.Same(ex, logger.Single.Exception);
    }

    [Fact]
    public void WriteCritical_NoArgs_WithException_AttachesExceptionAndLogsMessage()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("data corruption");

        logger.WriteCritical("Fatal: data store unrecoverable", ex);

        Assert.Equal(LogLevel.Critical, logger.Single.Level);
        Assert.Equal("Fatal: data store unrecoverable", logger.Single.Message);
        Assert.Same(ex, logger.Single.Exception);
    }

    [Fact]
    public void WriteError_NoArgs_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { MinLevel = LogLevel.Critical };

        logger.WriteError("should not appear", new Exception());

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void WriteCritical_NoArgs_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { Enabled = false };

        logger.WriteCritical("should not appear", new Exception());

        Assert.Empty(logger.Entries);
    }

    // -------------------------------------------------------------------------
    // WriteError / WriteCritical — typed, no exception (C# 13 OverloadResolutionPriority fix)
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteError_Typed_NoException_OneArg_LogsValueAndNoException()
    {
        TestLogger logger = new();

        logger.WriteError("Error happens here {Id}", 124);

        Assert.Equal(LogLevel.Error, logger.Single.Level);
        Assert.Equal("Error happens here 124", logger.Single.Message);
        Assert.Equal(124, logger.Single["Id"]);
        Assert.Null(logger.Single.Exception);
    }

    [Fact]
    public void WriteError_Typed_NoException_TwoArgs_LogsBothValues()
    {
        TestLogger logger = new();

        logger.WriteError("User {UserId} failed action {Action}", 42, "checkout");

        Assert.Equal(42, logger.Single["UserId"]);
        Assert.Equal("checkout", logger.Single["Action"]);
        Assert.Null(logger.Single.Exception);
    }

    [Fact]
    public void WriteError_Typed_NoException_ThreeArgs_LogsAllValues()
    {
        TestLogger logger = new();

        logger.WriteError("Order {Id} item {Sku} qty {Qty}", 7, "ABC", 3);

        Assert.Equal(7, logger.Single["Id"]);
        Assert.Equal("ABC", logger.Single["Sku"]);
        Assert.Equal(3, logger.Single["Qty"]);
        Assert.Null(logger.Single.Exception);
    }

    [Fact]
    public void WriteCritical_Typed_NoException_OneArg_LogsValueAndNoException()
    {
        TestLogger logger = new();

        logger.WriteCritical("Critical failure {Code}", 500);

        Assert.Equal(LogLevel.Critical, logger.Single.Level);
        Assert.Equal("Critical failure 500", logger.Single.Message);
        Assert.Equal(500, logger.Single["Code"]);
        Assert.Null(logger.Single.Exception);
    }

    [Fact]
    public void WriteError_WithException_StillRoutesToExceptionOverload()
    {
        // Verifies that [OverloadResolutionPriority(-1)] on the typed no-exception overload
        // does NOT shadow WriteError(message, Exception) when an exception is the second arg.
        TestLogger logger = new();
        InvalidOperationException ex = new("boom");

        logger.WriteError("Something failed", ex);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal(LogLevel.Error, logger.Single.Level);
    }

    [Fact]
    public void WriteCritical_WithException_StillRoutesToExceptionOverload()
    {
        TestLogger logger = new();
        InvalidOperationException ex = new("fatal");

        logger.WriteCritical("Fatal error", ex);

        Assert.Same(ex, logger.Single.Exception);
        Assert.Equal(LogLevel.Critical, logger.Single.Level);
    }

    [Fact]
    public void WriteError_Typed_NoException_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { Enabled = false };

        logger.WriteError("should not appear {Id}", 99);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void WriteCritical_Typed_NoException_WhenDisabled_DoesNothing()
    {
        TestLogger logger = new() { MinLevel = LogLevel.None };

        logger.WriteCritical("should not appear {Id}", 99);

        Assert.Empty(logger.Entries);
    }

}
