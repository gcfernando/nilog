// -----------------------------------------------------------------------------
//  Nilog.Analyzers.Tests — behavioural verification of the NILOG001 code fix (F-004/F-005).
//  For every case the ORIGINAL and the FIXED source are both compiled and executed against
//  a capturing logger; rendered message, exception identity and argument evaluation order
//  must be identical. Source-text assertions alone are not accepted as evidence.
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Nilog.Analyzers;

namespace Nilog.Analyzers.Tests;

public sealed class CapturingLogger : ILogger
{
    public List<string> Messages { get; } = [];
    public List<Exception?> Exceptions { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        Exceptions.Add(exception);
    }
}

public static class EvalTrace
{
    public static List<string> Order { get; } = [];

    public static int Next(string tag)
    {
        Order.Add(tag);
        return Order.Count;
    }

    public static string S(string tag)
    {
        Order.Add(tag);
        return tag;
    }
}

public class InterpolatedTemplateFixBehaviorTests
{
    private const string Header = """
        using Microsoft.Extensions.Logging;
        using Nilog;
        using Nilog.Analyzers.Tests;
        using System;

        public static class C
        {
            public static void Run(ILogger logger)
            {
                int x = 7; string s = "ab"; bool flag = true; var ex = new InvalidOperationException("boom"); object o = "obj";
        """;

    private const string Footer = """

            }
        }
        """;

    private static string Wrap(string statements) => Header + "\n" + statements + "\n" + Footer;

    public static IEnumerable<object[]> Cases() =>
    [
        ["literal braces", """logger.WriteInformation($"a {{b}} {x} }}c {{");"""],
        ["only braces", """logger.WriteInformation($"{{}}");"""],
        ["format clause", """logger.WriteInformation($"n={x:D4}");"""],
        ["alignment", """logger.WriteInformation($"[{s,6}] [{s,-6}]");"""],
        ["alignment and format", """logger.WriteInformation($"[{x,6:D3}]");"""],
        ["duplicate placeholders", """logger.WriteInformation($"{x} {x} {s}");"""],
        ["escapes and quotes", "logger.WriteInformation($\"line\\n\\\"q\\\" \\\\ {x}\");"],
        ["verbatim", "logger.WriteInformation($@\"C:\\dir\\{x} \"\"q\"\" {{b}}\");"],
        ["raw single line", "logger.WriteInformation($$\"\"\"he said \"hi\" {{x}} { } {\"\"\");"],
        ["nested interpolation", """logger.WriteInformation($"a {(flag ? $"y{x}" : "n")} z");"""],
        ["complex expressions", """logger.WriteInformation($"{x + 1} {s.Length} {(flag ? "t" : "f")}");"""],
        ["side effects in template", """logger.WriteInformation($"{EvalTrace.Next("a")} {EvalTrace.Next("b")}");"""],
        ["trailing args keep order (info)", """logger.WriteInformation($"A {EvalTrace.S("t1")}", EvalTrace.S("t2"), EvalTrace.S("t3"));"""],
        ["trailing arg (warning)", """logger.WriteWarning($"A {x} B {s}", EvalTrace.S("tail"));"""],
        ["exception overload", """logger.WriteError($"failed {x}", ex);"""],
        ["exception + trailing", """logger.WriteError($"failed {EvalTrace.S("t")}", ex, EvalTrace.S("tail"));"""],
        ["critical exception", """logger.WriteCritical($"dead {x}", ex);"""],
        ["static Log", """Nilogger.Log(logger, LogLevel.Warning, $"Retry {x}");"""],
        ["static Log exception", """Nilogger.Log(logger, LogLevel.Error, $"Retry {x}", ex);"""],
        ["static Log trailing", """Nilogger.Log(logger, LogLevel.Information, $"Retry {x}", EvalTrace.S("tail"));"""],
        ["static WriteInformation", """Nilogger.WriteInformation(logger, $"Static {x}");"""],
        ["named message", """logger.WriteInformation(message: $"named {x}");"""],
        ["no interpolation holes", """logger.WriteInformation($"plain {{text}}");"""],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FixedSource_BehavesIdenticallyToOriginal(string name, string statement)
    {
        string original = Wrap(statement);

        (List<string> msgs0, List<Exception?> ex0, List<string> order0) = Execute(original);
        string? fixedSource = await ApplyIterativelyAsync(original);

        Assert.True(fixedSource is not null, $"[{name}] fix was withheld unexpectedly");

        (List<string> msgs1, List<Exception?> ex1, List<string> order1) = Execute(fixedSource);

        Assert.Equal(msgs0, msgs1);
        Assert.Equal(order0, order1);
        Assert.Equal(ex0.Count, ex1.Count);
        for (int i = 0; i < ex0.Count; i++)
        {
            Assert.Equal(ex0[i]?.GetType(), ex1[i]?.GetType());
            Assert.Equal(ex0[i]?.Message, ex1[i]?.Message);
        }
    }

    [Fact]
    public async Task NullValue_RendersAsMelNullText_DocumentedDifference()
    {
        // The one intentional observable difference: C# interpolation renders null as "",
        // while a Nilog/MEL template renders "(null)". Documented, not a defect.
        string src = Wrap("""string? n = null; logger.WriteInformation($"v={n}");""");
        (List<string> before, _, _) = Execute(src);
        (List<string> after, _, _) = Execute((await ApplyIterativelyAsync(src))!);

        Assert.Equal("v=", before[0]);
        Assert.Equal("v=(null)", after[0]);
    }

    [Fact]
    public async Task ExtractedArgs_AreInsertedBeforeExistingTrailingArgs_F004()
    {
        string fixedSource = (await ApplyIterativelyAsync(Wrap("""logger.WriteInformation($"A {x}", s);""")))!;

        Assert.Contains("logger.WriteInformation(\"A {x}\", x, s)", fixedSource);
    }

    [Fact]
    public async Task ExceptionAndTrailing_ExtractedGoAfterException_F004()
    {
        string fixedSource = (await ApplyIterativelyAsync(Wrap("""logger.WriteError($"A {x}", ex, s);""")))!;

        Assert.Contains("logger.WriteError(\"A {x}\", ex, x, s)", fixedSource);
    }

    [Fact]
    public async Task LiteralBraces_AreEscapedOnce_F005()
    {
        string fixedSource = (await ApplyIterativelyAsync(Wrap("""logger.WriteInformation($"a {{b}} {x}");""")))!;

        Assert.Contains("\"a {{b}} {x}\", x)", fixedSource);
    }

    [Fact]
    public async Task OtherNamedArguments_WithholdTheFix()
    {
        string src = Wrap("""logger.WriteError($"A {x}", exception: ex);""");

        Assert.Null(await ApplyIterativelyAsync(src));
    }

    [Fact]
    public async Task ExplicitParamsArray_WithholdsTheFix()
    {
        string src = Wrap("""logger.WriteInformation($"A {x}", new object[] { s });""");

        Assert.Null(await ApplyIterativelyAsync(src));
    }

    [Fact]
    public async Task MultiLineRawString_WithholdsTheFix()
    {
        string src = Wrap("logger.WriteInformation($\"\"\"\n            line1 {x}\n            line2\n            \"\"\");");

        Assert.Null(await ApplyIterativelyAsync(src));
    }

    [Fact]
    public async Task FixAll_AppliesEveryOccurrenceInDocument()
    {
        string src = Wrap("""
            logger.WriteInformation($"one {x}", s);
            logger.WriteError($"two {s}", ex);
            Nilogger.Log(logger, LogLevel.Warning, $"three {x} {{b}}");
            """);

        string? fixedAll = await ApplyFixAllAsync(src);

        Assert.NotNull(fixedAll);
        Assert.DoesNotContain("$\"", fixedAll!.Substring(fixedAll.IndexOf("Run(", StringComparison.Ordinal)));
        Assert.Contains("\"one {x}\", x, s)", fixedAll);
        Assert.Contains("\"two {s}\", ex, s)", fixedAll);
        Assert.Contains("\"three {x} {{b}}\", x)", fixedAll);
        (List<string> m0, _, _) = Execute(src);
        (List<string> m1, _, _) = Execute(fixedAll);
        Assert.Equal(m0, m1);
    }

    // ----- harness -----

    private static readonly MetadataReference[] s_platformRefs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(System.IO.Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    private static MetadataReference[] References() =>
    [
        .. s_platformRefs,
        MetadataReference.CreateFromFile(typeof(ILogger).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(global::Nilog.Nilogger).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CapturingLogger).Assembly.Location),
    ];

    private static (List<string>, List<Exception?>, List<string>) Execute(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Exec" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        Assembly asm = Assembly.Load(ms.ToArray());
        var logger = new CapturingLogger();
        EvalTrace.Order.Clear();
        asm.GetType("C")!.GetMethod("Run")!.Invoke(null, [logger]);
        return (logger.Messages, logger.Exceptions, [.. EvalTrace.Order]);
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source)
    {
        Project project = workspace.CurrentSolution
            .AddProject("Test", "Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(References());
        return project.AddDocument("Test.cs", SourceText.From(source));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Document document)
    {
        Compilation compilation = (await document.Project.GetCompilationAsync())!;
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InterpolatedTemplateAnalyzer()));
        return (await withAnalyzers.GetAnalyzerDiagnosticsAsync())
            .Where(d => d.Id == InterpolatedTemplateAnalyzer.DiagnosticId)
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    // Applies the fix to the first remaining diagnostic until none are left; returns null when a fix is withheld.
    private static async Task<string?> ApplyIterativelyAsync(string source)
    {
        string current = source;
        for (int guard = 0; guard < 20; guard++)
        {
            using var workspace = new AdhocWorkspace();
            Document document = CreateDocument(workspace, current);
            ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(document);
            if (diagnostics.IsEmpty)
            {
                return current;
            }

            var provider = new InterpolatedTemplateCodeFixProvider();
            CodeAction? action = null;
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[0], (a, _) => action ??= a, CancellationToken.None));
            if (action is null)
            {
                return null;
            }

            var applied = (await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
            current = (await applied.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
        }

        throw new InvalidOperationException("fix did not converge");
    }

    private sealed class Provider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>([]);
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }

    private static async Task<string?> ApplyFixAllAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, source);
        ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(document);

        var codeFix = new InterpolatedTemplateCodeFixProvider();
        var context = new FixAllContext(
            document, codeFix, FixAllScope.Document, "Convert to a literal template with arguments",
            [InterpolatedTemplateAnalyzer.DiagnosticId], new Provider(diagnostics), CancellationToken.None);

        CodeAction? action = await codeFix.GetFixAllProvider()!.GetFixAsync(context);
        if (action is null)
        {
            return null;
        }

        var applied = (await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
        return (await applied.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }
}
