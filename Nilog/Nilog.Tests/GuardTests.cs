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
}
