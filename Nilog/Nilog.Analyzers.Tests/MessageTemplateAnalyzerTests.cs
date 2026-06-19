// -----------------------------------------------------------------------------
//  Nilog.Analyzers.Tests — verifies NILOG002 (placeholder/argument count
//  mismatch) and NILOG003 (concatenated / string.Format message templates).
//
//  File        : MessageTemplateAnalyzerTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Nilog.Analyzers;

namespace Nilog.Analyzers.Tests;

public class MessageTemplateAnalyzerTests
{
    private const string Usings = """
        using Microsoft.Extensions.Logging;
        using Nilog;
        using System;

        """;

    [Fact]
    public void PlaceholderCountMismatch_TooFewArguments_ReportsNilog002()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a)
                {
                    logger.WriteInformation("{A} {B}", a);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.ArgumentCountDiagnosticId);
    }

    [Fact]
    public void PlaceholderCountMatches_NoDiagnostic()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a, int b)
                {
                    logger.WriteInformation("{A} {B}", a, b);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.ArgumentCountDiagnosticId);
    }

    [Fact]
    public void PlaceholderWithNoArguments_OnStaticLog_ReportsNilog002()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger)
                {
                    Nilogger.Log(logger, LogLevel.Information, "value is {Value}");
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.ArgumentCountDiagnosticId);
    }

    [Fact]
    public void EscapedBraces_AreNotCountedAsPlaceholders()
    {
        // "{{literal}}" is an escaped brace pair, not a placeholder, so a no-argument call
        // must not be flagged.
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger)
                {
                    logger.WriteInformation("a {{literal}} brace");
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.ArgumentCountDiagnosticId);
    }

    [Fact]
    public void Concatenation_AsTemplate_ReportsNilog003()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation("User " + id + " signed in");
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.DynamicTemplateDiagnosticId);
    }

    [Fact]
    public void StringFormat_AsTemplate_ReportsNilog003()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation(string.Format("User {0} signed in", id));
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.DynamicTemplateDiagnosticId);
    }

    [Fact]
    public void ConstantTemplate_NoDynamicTemplateDiagnostic()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation("User {Id} signed in", id);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.DynamicTemplateDiagnosticId);
    }

    [Fact]
    public void ConstantConcatenationOfLiterals_NoDiagnostic()
    {
        // "a" + "b" folds to a compile-time constant, so it is a stable template - neither
        // NILOG002 (count matches: zero placeholders, zero args) nor NILOG003 should fire.
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger)
                {
                    logger.WriteInformation("Service " + "started");
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Id == MessageTemplateAnalyzer.ArgumentCountDiagnosticId ||
            d.Id == MessageTemplateAnalyzer.DynamicTemplateDiagnosticId);
    }

    [Fact]
    public void DuplicateNamedPlaceholder_ReportsNilog004()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a, int b)
                {
                    logger.WriteInformation("User {Id} then {Id}", a, b);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.DuplicatePlaceholderDiagnosticId);
    }

    [Fact]
    public void DistinctNamedPlaceholders_NoNilog004()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a, int b)
                {
                    logger.WriteInformation("{A} {B}", a, b);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.DuplicatePlaceholderDiagnosticId);
    }

    [Fact]
    public void NumericPositionalReuse_IsNotFlaggedAsDuplicate()
    {
        // "{0} {0}" legitimately reuses positional argument 0 - NILOG004 must not fire.
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a)
                {
                    logger.WriteInformation("{0} and {0}", a);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.DuplicatePlaceholderDiagnosticId);
    }

    // ---- NILOG005: positional placeholders ----

    [Fact]
    public void PositionalPlaceholders_ReportsNilog005()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a, int b)
                {
                    logger.WriteInformation("{0} {1}", a, b);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.PositionalPlaceholderDiagnosticId);
    }

    [Fact]
    public void NamedPlaceholders_NoNilog005()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int a, int b)
                {
                    logger.WriteInformation("{A} {B}", a, b);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.PositionalPlaceholderDiagnosticId);
    }

    // ---- NILOG006: exception passed as a template value ----

    [Fact]
    public void ExceptionAsTemplateValue_ReportsNilog006()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, Exception ex)
                {
                    logger.WriteInformation("Failed {Error}", ex);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.ExceptionAsValueDiagnosticId);
    }

    [Fact]
    public void ExceptionInExceptionParameter_NoNilog006()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, Exception ex, int id)
                {
                    logger.WriteError("Failed {Id}", ex, id);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.ExceptionAsValueDiagnosticId);
    }

    // ---- NILOG007: malformed template ----

    [Fact]
    public void UnclosedBrace_ReportsNilog007()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger)
                {
                    logger.WriteInformation("Unclosed {Brace");
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.MalformedTemplateDiagnosticId);
    }

    [Fact]
    public void EmptyPlaceholder_ReportsNilog007()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int x)
                {
                    logger.WriteInformation("Value {:N2}", x);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.MalformedTemplateDiagnosticId);
    }

    [Fact]
    public void WellFormedTemplate_NoNilog007()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation("User {Id} ok", id);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.MalformedTemplateDiagnosticId);
    }

    // ---- NILOG008: PascalCase placeholder names ----

    [Fact]
    public void LowercasePlaceholderName_ReportsNilog008()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int x)
                {
                    logger.WriteInformation("User {userId}", x);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == MessageTemplateAnalyzer.PascalCaseDiagnosticId);
    }

    [Fact]
    public void PascalCasePlaceholderName_NoNilog008()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int x)
                {
                    logger.WriteInformation("User {UserId}", x);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == MessageTemplateAnalyzer.PascalCaseDiagnosticId);
    }

    private static readonly MetadataReference[] s_platformRefs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(System.IO.Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    private static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);

        MetadataReference[] refs =
        [
            .. s_platformRefs,
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::Nilog.Nilogger).Assembly.Location),
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new MessageTemplateAnalyzer();
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
