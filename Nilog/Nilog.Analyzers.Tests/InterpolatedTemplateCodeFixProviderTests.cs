// -----------------------------------------------------------------------------
//  Nilog.Analyzers.Tests — applies the NILOG001 code fix end-to-end and asserts
//  the rewritten source: $"..." becomes a literal template with the interpolated
//  expressions appended as arguments, for every Nilog call shape.
//
//  File        : InterpolatedTemplateCodeFixProviderTests.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Nilog.Analyzers;

namespace Nilog.Analyzers.Tests;

public class InterpolatedTemplateCodeFixProviderTests
{
    private const string Usings = """
        using Microsoft.Extensions.Logging;
        using Nilog;
        using System;

        """;

    [Fact]
    public async Task SimpleInterpolation_IsRewrittenToTemplateAndArg()
    {
        string fixaed = await ApplyFixAsync(Usings + """
            class C
            {
                void M(ILogger logger, int id)
                {
                    logger.WriteInformation($"User {id} signed in");
                }
            }
            """);

        Assert.Contains("logger.WriteInformation(\"User {id} signed in\", id)", fixaed);
        Assert.DoesNotContain("$\"", fixaed);
    }

    [Fact]
    public async Task MultipleInterpolations_AppendInOrder()
    {
        string fixaed = await ApplyFixAsync(Usings + """
            class C
            {
                void M(ILogger logger, int id, string ip)
                {
                    logger.WriteInformation($"User {id} from {ip}");
                }
            }
            """);

        Assert.Contains("logger.WriteInformation(\"User {id} from {ip}\", id, ip)", fixaed);
    }

    [Fact]
    public async Task ExceptionOverload_AppendsArgsAfterException()
    {
        string fixaed = await ApplyFixAsync(Usings + """
            class C
            {
                void M(ILogger logger, Exception ex, int id)
                {
                    logger.WriteError($"Order {id} failed", ex);
                }
            }
            """);

        // The extracted value goes after the exception — the trailing-args position for this shape.
        Assert.Contains("logger.WriteError(\"Order {id} failed\", ex, id)", fixaed);
    }

    [Fact]
    public async Task FormatClause_IsPreservedInTemplate()
    {
        string fixaed = await ApplyFixAsync(Usings + """
            class C
            {
                void M(ILogger logger, decimal amt)
                {
                    logger.WriteInformation($"Amount {amt:N2}");
                }
            }
            """);

        Assert.Contains("logger.WriteInformation(\"Amount {amt:N2}\", amt)", fixaed);
    }

    [Fact]
    public async Task StaticLog_IsRewritten()
    {
        string fixaed = await ApplyFixAsync(Usings + """
            class C
            {
                void M(ILogger logger, int n)
                {
                    Nilogger.Log(logger, LogLevel.Warning, $"Retry {n}");
                }
            }
            """);

        Assert.Contains("Nilogger.Log(logger, LogLevel.Warning, \"Retry {n}\", n)", fixaed);
    }

    private static readonly MetadataReference[] s_platformRefs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(System.IO.Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    private static async Task<string> ApplyFixAsync(string source)
    {
        MetadataReference[] refs =
        [
            .. s_platformRefs,
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::Nilog.Nilogger).Assembly.Location),
        ];

        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        Project project = workspace.CurrentSolution
            .AddProject("Test", "Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(refs);
        Document document = project.AddDocument("Test.cs", SourceText.From(source));

        Compilation compilation = (await document.Project.GetCompilationAsync())!;
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new InterpolatedTemplateAnalyzer()));
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        Diagnostic diagnostic = diagnostics.First(d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId);

        var provider = new InterpolatedTemplateCodeFixProvider();
        CodeAction? action = null;
        var fixContext = new CodeFixContext(document, diagnostic,
            (a, _) => action ??= a, CancellationToken.None);
        await provider.RegisterCodeFixesAsync(fixContext);

        Assert.NotNull(action);
        ImmutableArray<CodeActionOperation> operations = await action!.GetOperationsAsync(CancellationToken.None);
        var applied = operations.OfType<ApplyChangesOperation>().Single();
        Document changed = applied.ChangedSolution.GetDocument(document.Id)!;
        SourceText text = await changed.GetTextAsync();
        return text.ToString();
    }
}
