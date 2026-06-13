// -----------------------------------------------------------------------------
//  Nilog tests — covers message-template parsing: named holes, brace escaping,
//  alignment/format specifiers, and the raw-template fallback on mismatch.
//
//  File        : TemplateTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
namespace Nilog.Tests;

/// <summary>Covers message-template parsing: names, escapes, alignment/format, and fallback.</summary>
public class TemplateTests
{
    [Fact]
    public void NamedPlaceholder_BecomesStructuredKey()
    {
        TestLogger logger = new();

        logger.WriteInformation("Hello {Name}", "world");

        Assert.Equal("Hello world", logger.Single.Message);
        Assert.Equal("world", logger.Single["Name"]);
    }

    [Fact]
    public void EscapedBraces_AreRenderedLiterally()
    {
        TestLogger logger = new();

        logger.WriteInformation("{{literal}} {Val}", 5);

        Assert.Equal("{literal} 5", logger.Single.Message);
        Assert.Equal(5, logger.Single["Val"]);
    }

    [Fact]
    public void FormatSuffix_IsApplied()
    {
        TestLogger logger = new();

        logger.WriteInformation("V={Val:000}", 7);

        Assert.Equal("V=007", logger.Single.Message);
        Assert.Equal(7, logger.Single["Val"]);
    }

    [Fact]
    public void AlignmentSuffix_IsApplied()
    {
        TestLogger logger = new();

        logger.WriteInformation("[{Val,5}]", 42);

        Assert.Equal("[   42]", logger.Single.Message);
        Assert.Equal(42, logger.Single["Val"]);
    }

    [Fact]
    public void TooFewArguments_FallsBackToRawTemplate()
    {
        TestLogger logger = new();

        // Template has two placeholders but only one value is supplied, so string.Format
        // would throw. The formatter must swallow that and return the raw template.
        logger.WriteInformation("{A} {B}", 5);

        Assert.Equal("{A} {B}", logger.Single.Message);
    }

    [Fact]
    public void NoPlaceholders_WithArg_RendersTemplateText()
    {
        TestLogger logger = new();

        logger.WriteInformation("static text", 99);

        Assert.Equal("static text", logger.Single.Message);
    }

    [Fact]
    public void OriginalFormat_IsPreserved()
    {
        TestLogger logger = new();

        logger.WriteInformation("User {Id} did {Action}", 1, "login");

        Assert.Equal("User {Id} did {Action}", logger.Single["{OriginalFormat}"]);
    }

    [Fact]
    public void RepeatedTemplate_UsesCachedFormatter_AndStillRendersEachTime()
    {
        TestLogger logger = new();

        logger.WriteInformation("Value {V}", 1);
        logger.WriteInformation("Value {V}", 2);
        logger.WriteInformation("Value {V}", 3);

        Assert.Equal("Value 1", logger.Entries[0].Message);
        Assert.Equal("Value 2", logger.Entries[1].Message);
        Assert.Equal("Value 3", logger.Entries[2].Message);
    }

    [Fact]
    public void NullArgument_IsTolerated()
    {
        TestLogger logger = new();

        logger.WriteInformation("Name={Name}", (string?)null);

        Assert.Equal("Name=", logger.Single.Message);
    }
}
