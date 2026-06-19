// -----------------------------------------------------------------------------
//  Nilog.Analyzers — the structured-logging correctness/usage rules that complement
//  NILOG001 (interpolated templates). Grounded in the established SerilogAnalyzer
//  rule set and Microsoft's CA2254 / LoggerMessage guidance:
//
//    NILOG002  placeholder count != argument count (renders raw, loses properties).
//    NILOG003  template built with concatenation or string.Format (cache miss).
//    NILOG004  duplicate named placeholder (structured-property key collision).
//    NILOG005  positional '{0}' placeholders instead of named '{Name}' (Info).
//    NILOG006  an Exception passed as a template value instead of the exception
//              parameter (loses type/message/stack as structured data).
//    NILOG007  malformed template (unclosed '{' or empty '{}' placeholder).
//    NILOG008  placeholder name is not PascalCase (Info, naming convention).
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
/// Reports the NILOG002-NILOG008 structured-logging rules on calls to Nilog's logging methods.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageTemplateAnalyzer : DiagnosticAnalyzer
{
    public const string ArgumentCountDiagnosticId = "NILOG002";
    public const string DynamicTemplateDiagnosticId = "NILOG003";
    public const string DuplicatePlaceholderDiagnosticId = "NILOG004";
    public const string PositionalPlaceholderDiagnosticId = "NILOG005";
    public const string ExceptionAsValueDiagnosticId = "NILOG006";
    public const string MalformedTemplateDiagnosticId = "NILOG007";
    public const string PascalCaseDiagnosticId = "NILOG008";

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

    private static readonly DiagnosticDescriptor PositionalPlaceholderRule = new(
        PositionalPlaceholderDiagnosticId,
        title: "Prefer named placeholders over positional ones",
        messageFormat: "Template uses positional '{{{0}}}' placeholders - prefer named '{{Name}}' placeholders so " +
                       "each value becomes a queryable structured property",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Positional placeholders such as '{0}' render correctly but produce structured properties " +
                     "named \"0\", \"1\", ... which are not meaningful to query in a structured sink. Named " +
                     "placeholders like '{UserId}' capture intent and become useful properties.");

    private static readonly DiagnosticDescriptor ExceptionAsValueRule = new(
        ExceptionAsValueDiagnosticId,
        title: "Pass the exception to the exception parameter, not as a template value",
        messageFormat: "'{0}' is an exception passed as a template value - pass it as the exception parameter " +
                       "(e.g. WriteError(message, exception, ...)) so its type, message, and stack trace are captured.",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When an exception is passed as an ordinary template value it is rendered with ToString() and " +
                     "its structured exception data (type, stack trace, inner exceptions) is lost. Nilog's " +
                     "WriteError/WriteCritical and Nilogger.Log overloads accept the exception in a dedicated " +
                     "parameter that attaches it to the log entry for sinks to index.");

    private static readonly DiagnosticDescriptor MalformedTemplateRule = new(
        MalformedTemplateDiagnosticId,
        title: "Malformed Nilog message template",
        messageFormat: "Message template is malformed (an unclosed '{{' or an empty '{{}}' placeholder) - it will " +
                       "render literally and produce no structured property",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A template with an unclosed brace or an empty placeholder cannot bind a structured property " +
                     "and renders as raw text. Close every '{' with a matching '}', give each placeholder a name, " +
                     "and escape literal braces as '{{' and '}}'.");

    private static readonly DiagnosticDescriptor PascalCaseRule = new(
        PascalCaseDiagnosticId,
        title: "Use PascalCase for placeholder names",
        messageFormat: "Placeholder '{{{0}}}' should be PascalCase (e.g. '{{{1}}}') to match structured-logging conventions.",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Structured-logging convention (shared with Serilog) names template properties in PascalCase, " +
                     "so property names are consistent across sinks and queries. This is a style suggestion and " +
                     "never affects behaviour.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        ArgumentCountRule, DynamicTemplateRule, DuplicatePlaceholderRule, PositionalPlaceholderRule,
        ExceptionAsValueRule, MalformedTemplateRule, PascalCaseRule,
    ];

    private static readonly string[] LogMethodNames =
    [
        "WriteTrace", "WriteDebug", "WriteInformation", "WriteWarning",
        "WriteError", "WriteCritical", "Log",
    ];

    private static readonly string[] MessageParameterNames = ["message", "messageTemplate"];

    private static readonly char[] s_suffixChars = [',', ':'];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

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

        // NILOG006: an exception passed as a template value. Independent of the template's form,
        // so this runs before the interpolated/dynamic early-returns below.
        ReportExceptionAsValue(context, args, method, messageParamIndex);

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

        // The remaining rules need the constant template string.
        Optional<object?> constant = context.SemanticModel.GetConstantValue(expr, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not string template)
        {
            return;
        }

        // NILOG007: an unclosed '{' or an empty '{}' placeholder.
        if (IsMalformedTemplate(template))
        {
            context.ReportDiagnostic(Diagnostic.Create(MalformedTemplateRule, expr.GetLocation()));
        }

        List<string> names = ExtractPlaceholderNames(template);

        // NILOG004: a named placeholder used more than once.
        ReportDuplicateNamedPlaceholders(context, expr, names);

        // NILOG005 (positional) + NILOG008 (PascalCase) naming/usage suggestions.
        ReportNamingSuggestions(context, expr, names);

        // NILOG002: placeholder count differs from the argument count.
        if (!TryCountValueArguments(method, args, messageParamIndex, out int argCount))
        {
            return;
        }

        if (names.Count != argCount)
        {
            context.ReportDiagnostic(Diagnostic.Create(ArgumentCountRule, expr.GetLocation(), names.Count, argCount));
        }
    }

    // NILOG006: report each argument, in a template-value position, whose type derives from
    // System.Exception. The dedicated `exception` parameter is excluded so the correct usage
    // (WriteError(message, exception, ...)) is never flagged.
    private static void ReportExceptionAsValue(
        SyntaxNodeAnalysisContext context, SeparatedSyntaxList<ArgumentSyntax> args, IMethodSymbol method, int messageParamIndex)
    {
        foreach (ArgumentSyntax a in args)
        {
            if (a.NameColon is not null)
            {
                return; // named arguments make positional mapping unreliable; fail open
            }
        }

        int exceptionParamOrdinal = -1;
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (method.Parameters[i].Name == "exception")
            {
                exceptionParamOrdinal = i;
                break;
            }
        }

        for (int idx = messageParamIndex + 1; idx < args.Count; idx++)
        {
            if (idx == exceptionParamOrdinal)
            {
                continue;
            }

            ITypeSymbol? type = context.SemanticModel.GetTypeInfo(args[idx].Expression, context.CancellationToken).Type;
            if (type is not null && InheritsFromException(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(ExceptionAsValueRule, args[idx].GetLocation(), type.Name));
            }
        }
    }

    private static bool InheritsFromException(ITypeSymbol type)
    {
        for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == "System.Exception")
            {
                return true;
            }
        }

        return false;
    }

    // NILOG005 (any positional placeholder) + NILOG008 (a name whose first letter is lowercase).
    private static void ReportNamingSuggestions(
        SyntaxNodeAnalysisContext context, ExpressionSyntax expr, List<string> names)
    {
        bool positionalReported = false;
        HashSet<string> pascalReported = new(System.StringComparer.Ordinal);

        foreach (string name in names)
        {
            if (name.Length == 0)
            {
                continue;
            }

            if (IsAllDigits(name))
            {
                if (!positionalReported)
                {
                    positionalReported = true;
                    context.ReportDiagnostic(Diagnostic.Create(PositionalPlaceholderRule, expr.GetLocation(), name));
                }

                continue;
            }

            if (char.IsLetter(name[0]) && char.IsLower(name[0]) && pascalReported.Add(name))
            {
                context.ReportDiagnostic(Diagnostic.Create(PascalCaseRule, expr.GetLocation(), name, ToPascalCase(name)));
            }
        }
    }

    private static string ToPascalCase(string name)
    {
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
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

        HashSet<string> seen = new(System.StringComparer.Ordinal);
        HashSet<string> reported = new(System.StringComparer.Ordinal);
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

        return (expr is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
            || (expr is InvocationExpressionSyntax invocation
                && model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol called
                && called.Name == "Format"
                && called.ContainingType?.SpecialType == SpecialType.System_String);
    }

    // True when the template has an unclosed '{' (not part of "{{") or an empty placeholder
    // whose name trims to nothing (e.g. "{}", "{ }", "{:N2}").
    private static bool IsMalformedTemplate(string template)
    {
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
                    return true; // unclosed '{'
                }

                string token = template.Substring(i + 1, close - i - 1);
                int suffix = token.IndexOfAny(s_suffixChars);
                string name = (suffix < 0 ? token : token.Substring(0, suffix)).Trim();
                if (name.Length == 0)
                {
                    return true; // empty / suffix-only placeholder
                }

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
        List<string> names = [];
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
}
