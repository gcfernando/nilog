// -----------------------------------------------------------------------------
//  Nilog.Analyzers — two further structured-logging correctness rules that
//  complement NILOG001:
//
//    NILOG002  the number of '{Named}' placeholders in a constant template does
//              not match the number of arguments supplied at the call site, which
//              renders to the raw template (FormatException is swallowed) and
//              produces wrong/missing structured properties.
//
//    NILOG003  the message template is built with string concatenation or
//              string.Format(...). Like an interpolated string (NILOG001) this
//              produces a different template value on most calls, so it misses the
//              template cache and loses named structured properties.
//
//  File        : MessageTemplateAnalyzer.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nilog.Analyzers;

/// <summary>
/// Reports NILOG002 (placeholder/argument count mismatch) and NILOG003 (a
/// concatenated or <c>string.Format</c>-built message template) on calls to Nilog's
/// logging methods.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageTemplateAnalyzer : DiagnosticAnalyzer
{
    public const string ArgumentCountDiagnosticId = "NILOG002";
    public const string DynamicTemplateDiagnosticId = "NILOG003";
    public const string DuplicatePlaceholderDiagnosticId = "NILOG004";

    private static readonly DiagnosticDescriptor ArgumentCountRule = new(
        ArgumentCountDiagnosticId,
        title: "Template placeholder count does not match the number of arguments",
        messageFormat: "Message template has {0} placeholder(s) but {1} argument(s) are supplied - the call will " +
                       "render the raw template and lose structured properties",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Nilog (like Microsoft.Extensions.Logging) maps template placeholders to arguments by " +
                     "position. When the counts differ, string.Format throws internally, Nilog falls back to the " +
                     "un-rendered template, and the structured properties are wrong or missing. Make the number " +
                     "of '{Name}' placeholders match the number of arguments passed.");

    private static readonly DiagnosticDescriptor DynamicTemplateRule = new(
        DynamicTemplateDiagnosticId,
        title: "Avoid building a Nilog message template with concatenation or string.Format",
        messageFormat: "Pass a constant template with '{Name}' placeholders and separate arguments instead of a " +
                       "concatenated or string.Format-built string - it defeats Nilog's template cache and loses " +
                       "structured properties",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Nilog parses and caches each distinct template string once and turns its '{Name}' " +
                     "placeholders into named structured properties. A template assembled with '+' or " +
                     "string.Format produces a different value on most calls, so every call misses the cache, " +
                     "grows it, and the embedded values never become named properties. Use " +
                     "WriteInformation(\"User {Id} signed in\", id) instead of " +
                     "WriteInformation(\"User \" + id + \" signed in\").");

    private static readonly DiagnosticDescriptor DuplicatePlaceholderRule = new(
        DuplicatePlaceholderDiagnosticId,
        title: "Duplicate named placeholder in a Nilog message template",
        messageFormat: "Placeholder '{{{0}}}' appears more than once - structured sinks key by name, so the " +
                       "duplicate property silently overwrites or collides; give each placeholder a distinct name",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each '{Name}' placeholder becomes a named structured property. When the same name appears " +
                     "twice, structured sinks (Serilog, Seq, OpenTelemetry, Application Insights) keep only one " +
                     "value for that key, so data is silently lost. Numeric/positional placeholders ({0} {0}) are " +
                     "not flagged because reusing a positional argument is legitimate.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ArgumentCountRule, DynamicTemplateRule, DuplicatePlaceholderRule);

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

        if (method.ContainingType?.ToDisplayString() != "Nilog.Nilogger")
        {
            return;
        }

        int messageParamIndex = -1;
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (System.Array.IndexOf(MessageParameterNames, method.Parameters[i].Name) >= 0)
            {
                messageParamIndex = i;
                break;
            }
        }

        if (messageParamIndex < 0)
        {
            return;
        }

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;

        // Locate the message argument syntax. A named argument can appear anywhere; otherwise
        // it sits at the same positional index as the (reduced) message parameter.
        ArgumentSyntax? messageArg = null;
        foreach (ArgumentSyntax a in args)
        {
            if (a.NameColon?.Name.Identifier.Text is string n && System.Array.IndexOf(MessageParameterNames, n) >= 0)
            {
                messageArg = a;
                break;
            }
        }

        if (messageArg is null && messageParamIndex < args.Count && args[messageParamIndex].NameColon is null)
        {
            messageArg = args[messageParamIndex];
        }

        if (messageArg is null)
        {
            return;
        }

        ExpressionSyntax expr = messageArg.Expression;

        // Interpolated strings are NILOG001's job; skip them here so we never double-report.
        if (expr is InterpolatedStringExpressionSyntax)
        {
            return;
        }

        // NILOG003: a template assembled with '+' concatenation or string.Format(...).
        if (IsDynamicTemplate(expr, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(DynamicTemplateRule, expr.GetLocation()));
            return;
        }

        // NILOG002: a constant template whose placeholder count differs from the argument count.
        Optional<object?> constant = context.SemanticModel.GetConstantValue(expr, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not string template)
        {
            return;
        }

        List<string> names = ExtractPlaceholderNames(template);

        // NILOG004: a named placeholder used more than once (independent of argument count).
        ReportDuplicateNamedPlaceholders(context, expr, names);

        // NILOG002: a constant template whose placeholder count differs from the argument count.
        if (!TryCountValueArguments(method, args, messageParamIndex, out int argCount))
        {
            return;
        }

        if (names.Count != argCount)
        {
            context.ReportDiagnostic(Diagnostic.Create(ArgumentCountRule, expr.GetLocation(), names.Count, argCount));
        }
    }

    // Flags the first named placeholder that appears more than once. Purely numeric/positional
    // names ({0} {0}) are skipped because reusing a positional argument is legitimate.
    private static void ReportDuplicateNamedPlaceholders(
        SyntaxNodeAnalysisContext context, ExpressionSyntax expr, List<string> names)
    {
        if (names.Count < 2)
        {
            return;
        }

        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var reported = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (name.Length == 0 || IsAllDigits(name))
            {
                continue;
            }

            if (!seen.Add(name) && reported.Add(name))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicatePlaceholderRule, expr.GetLocation(), name));
            }
        }
    }

    private static bool IsAllDigits(string s)
    {
        foreach (char c in s)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    // True for templates that vary at runtime: "a" + b or string.Format(...). Constant
    // concatenations ("a" + "b", or const fields) are folded by GetConstantValue and excluded.
    private static bool IsDynamicTemplate(ExpressionSyntax expr, SemanticModel model, System.Threading.CancellationToken ct)
    {
        if (model.GetConstantValue(expr, ct).HasValue)
        {
            return false;
        }

        if (expr is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            return true;
        }

        if (expr is InvocationExpressionSyntax invocation &&
            model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol called &&
            called.Name == "Format" &&
            called.ContainingType?.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        return false;
    }

    // Determines how many template-value arguments the call supplies, for both the typed
    // overloads (a fixed number of value parameters) and the params object[] overload (the
    // trailing call-site arguments). Returns false when the count cannot be determined
    // safely - e.g. named arguments or an explicit array passed to the params slot - so the
    // rule fails open rather than risk a false positive.
    private static bool TryCountValueArguments(
        IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> args, int messageParamIndex, out int count)
    {
        count = 0;

        foreach (ArgumentSyntax a in args)
        {
            if (a.NameColon is not null)
            {
                return false;
            }
        }

        IParameterSymbol last = method.Parameters[method.Parameters.Length - 1];
        if (last.IsParams)
        {
            int paramsOrdinal = method.Parameters.Length - 1;
            int supplied = args.Count - paramsOrdinal;
            if (supplied < 0)
            {
                return false;
            }

            // A single argument in the params slot may be an explicit object[] rather than a
            // spread of values; we cannot count its elements reliably, so skip.
            if (supplied == 1 && args.Count > paramsOrdinal)
            {
                ExpressionSyntax single = args[paramsOrdinal].Expression;
                if (single is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax ||
                    single.IsKind(SyntaxKind.CollectionExpression))
                {
                    return false;
                }
            }

            count = supplied;
            return true;
        }

        // Typed overload: value parameters are everything after the message parameter that is
        // not the attached exception.
        int valueCount = 0;
        for (int i = messageParamIndex + 1; i < method.Parameters.Length; i++)
        {
            IParameterSymbol p = method.Parameters[i];
            if (p.Name == "exception" || p.Type.Name == "Exception")
            {
                continue;
            }

            valueCount++;
        }

        count = valueCount;
        return true;
    }

    // Extracts the name portion of each '{...}' placeholder, honouring "{{"/"}}" escapes and
    // stripping any ",alignment"/":format" suffix. Mirrors the tokenizer in
    // Nilogger.TemplateFormatter so the analyzer and the runtime agree. The list count is also
    // the placeholder count used by NILOG002.
    private static List<string> ExtractPlaceholderNames(string template)
    {
        var names = new List<string>();
        int i = 0;
        int n = template.Length;

        while (i < n)
        {
            char c = template[i];
            if (c == '{')
            {
                if (i + 1 < n && template[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    break;
                }

                string token = template.Substring(i + 1, close - i - 1);
                int suffix = token.IndexOfAny(s_suffixChars);
                string name = (suffix < 0 ? token : token.Substring(0, suffix)).Trim();
                names.Add(name);
                i = close + 1;
            }
            else if (c == '}' && i + 1 < n && template[i + 1] == '}')
            {
                i += 2;
            }
            else
            {
                i++;
            }
        }

        return names;
    }

    private static readonly char[] s_suffixChars = { ',', ':' };
}
