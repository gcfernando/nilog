// -----------------------------------------------------------------------------
//  Nilog tests — F-001 regression: a leading Exception passed to the typed generic
//  WriteError/WriteCritical/Log overloads (which is what C# 12 binds a derived
//  exception to) must be attached as the log exception, never rendered as a value.
//  Calls use explicit type arguments to force the generic overloads regardless of
//  the language version compiling this test.
//
//  File        : ExceptionBindingTests.cs
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

public class ExceptionBindingTests
{
    private static readonly InvalidOperationException Ex = new("boom");

    [Fact]
    public void WriteError_GenericDerivedException_Attached()
    {
        TestLogger l = new();
        l.WriteError<InvalidOperationException>("msg", Ex);
        Assert.Same(Ex, l.Single.Exception);
        Assert.Equal("msg", l.Single.Message);
    }

    [Fact]
    public void WriteError_GenericDerivedExceptionWithArgs_AttachedAndRemainingArgsBound()
    {
        TestLogger l = new();
        l.WriteError<InvalidOperationException, int>("a {A}", Ex, 5);
        l.WriteError<InvalidOperationException, int, int>("a {A} {B}", Ex, 5, 6);
        l.WriteError<InvalidOperationException, int, int, int>("a {A} {B} {C}", Ex, 5, 6, 7);
        l.WriteError<InvalidOperationException, int, int, int, int>("a {A} {B} {C} {D}", Ex, 5, 6, 7, 8);
        l.WriteError<InvalidOperationException, int, int, int, int, int>("a {A} {B} {C} {D} {E}", Ex, 5, 6, 7, 8, 9);
        Assert.All(l.Entries, e => Assert.Same(Ex, e.Exception));
        Assert.Equal("a 5", l.Entries[0].Message);
        Assert.Equal("a 5 6 7 8 9", l.Entries[4].Message);
        Assert.Equal(5, l.Entries[0]["A"]);
    }

    [Fact]
    public void WriteCritical_GenericDerivedException_Attached()
    {
        TestLogger l = new();
        l.WriteCritical<InvalidOperationException, int>("a {A}", Ex, 1);
        l.WriteCritical<InvalidOperationException, int, int>("a {A} {B}", Ex, 1, 2);
        Assert.All(l.Entries, e => Assert.Same(Ex, e.Exception));
        Assert.Equal(LogLevel.Critical, l.Last.Level);
    }

    [Fact]
    public void WriteError_HighArity_GenericDerivedException_Attached()
    {
        TestLogger l = new();
        l.WriteError<InvalidOperationException, int, int, int, int, int, int>("{A}{B}{C}{D}{E}{F}", Ex, 1, 2, 3, 4, 5, 6);
        l.WriteCritical<InvalidOperationException, int, int, int, int, int, int, int>("{A}{B}{C}{D}{E}{F}{G}", Ex, 1, 2, 3, 4, 5, 6, 7);
        Assert.All(l.Entries, e => Assert.Same(Ex, e.Exception));
        Assert.Equal("123456", l.Entries[0].Message);
        Assert.Equal("1234567", l.Entries[1].Message);
    }

    [Fact]
    public void StaticLog_GenericDerivedException_Attached()
    {
        TestLogger l = new();
        Nilogger.Log<InvalidOperationException, int>(l, LogLevel.Warning, "x {A}", Ex, 3);
        Nilogger.Log<InvalidOperationException>(l, LogLevel.Warning, "x", Ex);
        Assert.All(l.Entries, e => Assert.Same(Ex, e.Exception));
        Assert.Equal("x 3", l.Entries[0].Message);
    }

    [Fact]
    public void StaticLog_ObjectArrayLeadingException_Attached()
    {
        TestLogger l = new();
        Nilogger.Log(l, LogLevel.Error, "x {A}", new object[] { Ex, 3 });
        Assert.Same(Ex, l.Single.Exception);
        Assert.Equal("x 3", l.Single.Message);
    }

    [Fact]
    public void ExceptionNotFirst_IsStillAValue()
    {
        TestLogger l = new();
        l.WriteError<int, InvalidOperationException>("{A} {B}", 1, Ex);
        Assert.Null(l.Single.Exception);
    }

    [Fact]
    public void NonExceptionValueTypes_Unaffected()
    {
        TestLogger l = new();
        l.WriteError<int>("{A}", 5);
        l.WriteError<string>("{A}", "s");
        Assert.All(l.Entries, e => Assert.Null(e.Exception));
    }

    [Fact]
    public void DisabledPath_DerivedExceptionGeneric_AllocatesZero()
    {
        TestLogger l = new() { MinLevel = LogLevel.Critical + 1 };
        for (int i = 0; i < 50; i++) { l.WriteError<InvalidOperationException, int>("{A}", Ex, 1); }
        long b = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            l.WriteError<InvalidOperationException, int>("{A}", Ex, 1);
            l.WriteError<int, int>("{A}{B}", 1, 2);
        }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - b);
    }
}
