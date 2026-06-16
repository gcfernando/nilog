// -----------------------------------------------------------------------------
//  Nilog.Analyzers.Tests — verifies NILOG001 fires only for interpolated
//  strings passed as a Nilog message template, and never for anything else.
//
//  File        : InterpolatedTemplateAnalyzerTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Nilog.Analyzers;

namespace Nilog.Analyzers.Tests;

public class InterpolatedTemplateAnalyzerTests
{
    private const string Usings = """
        using Microsoft.Extensions.Logging;
        using Nilog;
        using System;

        """;

    [Fact]
    public void InterpolatedTemplate_OnWriteInformation_ReportsDiagnostic()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation($"User {id} signed in");
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);
    }

    [Fact]
    public void LiteralTemplate_OnWriteInformation_NoDiagnostic()
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

        Assert.DoesNotContain(diagnostics, d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);
    }

    [Fact]
    public void InterpolatedTemplate_OnStaticNilogggerLog_ReportsDiagnostic()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    Nilogger.Log(logger, LogLevel.Information, $"User {id} signed in");
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);
    }

    [Fact]
    public void InterpolatedString_OnUnrelatedMethod_NoDiagnostic()
    {
        // Sanity check: the analyzer must not fire on every interpolated string in the
        // file, only on the ones passed to a Nilog log method.
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(int id)
                {
                    Console.WriteLine($"User {id} signed in");
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);
    }

    [Fact]
    public void InterpolatedTemplate_WithExceptionOverload_ReportsDiagnostic()
    {
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, Exception ex, int id)
                {
                    logger.WriteError($"Order {id} failed", ex);
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);
    }

    [Fact]
    public void NonInterpolatedConcatenation_NoDiagnostic()
    {
        // Plain string concatenation isn't interpolation - out of scope for this rule,
        // but must not be misclassified as a false positive either.
        ImmutableArray<Diagnostic> diagnostics = GetDiagnostics(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation("User " + id + " signed in");
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);
    }

    // The BCL is split across many facade assemblies (System.Runtime, System.Collections,
    // ...) that aren't all reachable just from typeof(object).Assembly. Pulling every
    // trusted platform assembly from the running runtime guarantees full coverage - the
    // standard pattern for in-memory Roslyn test compilations - instead of an
    // under-specified hand-picked list that fails closed (OverloadResolutionFailure,
    // not a real ambiguity) on basic types like Exception or Enum.
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

        var analyzer = new InterpolatedTemplateAnalyzer();
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
