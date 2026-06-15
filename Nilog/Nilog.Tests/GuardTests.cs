// -----------------------------------------------------------------------------
//  Nilog tests — covers argument validation across the public surface: null
//  loggers, messages, exceptions, and scope keys.
//
//  File        : GuardTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

/// <summary>Covers argument validation across the public surface.</summary>
public class GuardTests
{
    private static readonly ILogger Null = null!;

    [Fact]
    public void Log_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Nilogger.Log(Null, LogLevel.Information, "x"));
    }

    [Fact]
    public void LogTyped_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Nilogger.Log(Null, LogLevel.Information, "x {A}", 1));
    }

    [Fact]
    public void LogExceptionFirst_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => Nilogger.Log(Null, LogLevel.Error, new Exception(), "x", 1));
    }

    [Fact]
    public void LogExceptionFirst_NullException_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(
            () => Nilogger.Log(logger, LogLevel.Error, (Exception)null!, "x", 1));
    }

    [Fact]
    public void WriteInformation_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteInformation("x"));
    }

    [Fact]
    public void WriteInformation_NullMessage_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteInformation(null!));
    }

    [Fact]
    public void WriteInformationTyped_NullMessage_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteInformation(null!, 1));
    }

    [Fact]
    public void WriteError_NullException_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteError("msg", (Exception)null!));
    }

    [Fact]
    public void WriteErrorTyped_NullException_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteError("msg {A}", null!, 1));
    }

    [Fact]
    public void WriteErrorException_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteErrorException(new Exception()));
    }

    [Fact]
    public void WriteErrorException_NullException_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteErrorException(null!));
    }

    [Fact]
    public void WriteScope_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteScope("k", "v"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void WriteScope_BadKey_Throws(string? key)
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentException>(() => logger.WriteScope(key!, "v"));
    }

    [Fact]
    public void WriteScope_NullLogger_Dictionary_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(
            () => Null.WriteScope(new Dictionary<string, object> { ["k"] = "v" }));
    }

    // Feature C guards — the dedicated no-args overloads must validate their arguments.
    [Fact]
    public void WriteError_NoArgs_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteError("msg", new Exception()));
    }

    [Fact]
    public void WriteError_NoArgs_NullMessage_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteError(null!, new Exception()));
    }

    [Fact]
    public void WriteError_NoArgs_NullException_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteError("msg", (Exception)null!));
    }

    [Fact]
    public void WriteCritical_NoArgs_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteCritical("msg", new Exception()));
    }

    [Fact]
    public void WriteCritical_NoArgs_NullMessage_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteCritical(null!, new Exception()));
    }

    [Fact]
    public void WriteCritical_NoArgs_NullException_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteCritical("msg", (Exception)null!));
    }

    // Feature A guard — IEnumerable overload must validate its logger argument.
    [Fact]
    public void WriteScope_NullLogger_Enumerable_Throws()
    {
        IReadOnlyDictionary<string, object> ctx = new Dictionary<string, object> { ["k"] = "v" };
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteScope(ctx));
    }

    // v1.0.1 typed no-exception overloads — null logger and null message guards.
    // WriteError(null!, value) routes to WriteError<T0>(string, T0) because priority 0
    // (with-exception) has no applicable candidate when the second arg is not an Exception.

    [Fact]
    public void WriteError_TypedNoException_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteError("msg {A}", 1));
    }

    [Fact]
    public void WriteError_TypedNoException_NullMessage_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteError(null!, 1));
    }

    [Fact]
    public void WriteCritical_TypedNoException_NullLogger_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => Null.WriteCritical("msg {A}", 1));
    }

    [Fact]
    public void WriteCritical_TypedNoException_NullMessage_Throws()
    {
        TestLogger logger = new();
        _ = Assert.Throws<ArgumentNullException>(() => logger.WriteCritical(null!, 1));
    }

}
