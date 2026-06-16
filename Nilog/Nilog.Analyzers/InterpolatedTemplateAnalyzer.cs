// -----------------------------------------------------------------------------
//  Nilog.Analyzers — flags interpolated strings passed as a Nilog message
//  template, which defeats the zero-allocation template cache and breaks
//  structured logging (every call produces a different template string).
//
//  File        : InterpolatedTemplateAnalyzer.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nilog.Analyzers;

/// <summary>
/// Reports NILOG001 when a call to one of Nilog's logging methods passes an
/// interpolated string (<c>$"..."</c>) as the message template argument.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterpolatedTemplateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NILOG001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Avoid interpolated strings as Nilog message templates",
        messageFormat: "Pass a literal template with '{Name}' placeholders and separate arguments instead of an " +
                       "interpolated string - interpolation defeats Nilog's zero-allocation template cache and " +
                       "loses structured properties",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Nilog parses and caches each distinct message template string once. An interpolated " +
                      "string produces a different literal value on every call, so every call misses the " +
                      "cache, grows it without bound, and the interpolated values never become named " +
                      "structured properties the way placeholder arguments would. Use " +
                      "WriteInformation(\"User {Id} signed in\", id) instead of " +
                      "WriteInformation($\"User {id} signed in\").");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    // Every Nilog entry point that takes a message template - the convenience
    // extension methods and the static runtime-level API - shares this set of names.
    private static readonly string[] LogMethodNames =
    {
        "WriteTrace", "WriteDebug", "WriteInformation", "WriteWarning",
        "WriteError", "WriteCritical", "Log",
    };

    private static readonly string[] MessageParameterNames = { "message", "messageTemplate" };

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (System.Array.IndexOf(LogMethodNames, method.Name) < 0)
        {
            return;
        }

        // Every Nilog log method - the Write* extensions and the static Log overloads -
        // is physically declared on Nilog.Nilogger, so this one check covers both the
        // `logger.WriteInformation(...)` and `Nilogger.Log(...)` call shapes.
        if (method.ContainingType?.ToDisplayString() != "Nilog.Nilogger")
        {
            return;
        }

        int paramIndex = -1;
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (System.Array.IndexOf(MessageParameterNames, method.Parameters[i].Name) >= 0)
            {
                paramIndex = i;
                break;
            }
        }

        if (paramIndex < 0)
        {
            return;
        }

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;

        // For an extension method called via instance syntax (logger.WriteInformation(...)),
        // the symbol Roslyn hands back is the *reduced* form: its Parameters already exclude
        // the receiver, exactly like invocation.ArgumentList.Arguments excludes it too - so
        // paramIndex lines up with the syntax argument list directly in both call shapes
        // (the reduced extension form and the static Nilogger.Log(...) form).
        ArgumentSyntax? messageArg = null;
        foreach (ArgumentSyntax a in args)
        {
            if (a.NameColon?.Name.Identifier.Text is string n && System.Array.IndexOf(MessageParameterNames, n) >= 0)
            {
                messageArg = a;
                break;
            }
        }

        if (messageArg is null && paramIndex < args.Count && args[paramIndex].NameColon is null)
        {
            messageArg = args[paramIndex];
        }

        if (messageArg?.Expression is InterpolatedStringExpressionSyntax interpolated && HasInterpolations(interpolated))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, interpolated.GetLocation()));
        }
    }

    private static bool HasInterpolations(InterpolatedStringExpressionSyntax s)
    {
        foreach (InterpolatedStringContentSyntax content in s.Contents)
        {
            if (content is InterpolationSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
