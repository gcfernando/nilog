// -----------------------------------------------------------------------------
//  Nilog.Analyzers — code fix for NILOG001. Rewrites an interpolated string used
//  as a Nilog message template into a literal '{Name}' template plus the
//  interpolated expressions appended as separate arguments, e.g.
//
//      logger.WriteInformation($"User {id} from {ip}");
//  ->  logger.WriteInformation("User {id} from {ip}", id, ip);
//
//  The extracted values are inserted directly after the message (and after the exception
//  argument for the exception-aware shapes), ahead of any pre-existing trailing arguments.
//
//  File        : InterpolatedTemplateCodeFixProvider.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Composition;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nilog.Analyzers;

/// <summary>
/// Provides the "Convert to template with arguments" fix for <see cref="InterpolatedTemplateAnalyzer"/>
/// (NILOG001): turns <c>$"User {id}"</c> into <c>"User {id}", id</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InterpolatedTemplateCodeFixProvider)), Shared]
public sealed class InterpolatedTemplateCodeFixProvider : CodeFixProvider
{
    private const string Title = "Convert to a literal template with arguments";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(InterpolatedTemplateAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        Diagnostic diagnostic = context.Diagnostics[0];
        SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        InterpolatedStringExpressionSyntax? interpolated =
            node as InterpolatedStringExpressionSyntax ?? node.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>();

        ArgumentSyntax? argument = interpolated?.FirstAncestorOrSelf<ArgumentSyntax>();
        InvocationExpressionSyntax? invocation = interpolated?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (interpolated is null || argument is null || invocation is null)
        {
            return;
        }

        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null || !TryComputeFix(model, invocation, argument, interpolated, out FixPlan plan, context.CancellationToken))
        {
            // A transformation that cannot be proven behaviour-preserving is withheld; the
            // NILOG001 diagnostic still guides the developer to rewrite the call by hand.
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => Task.FromResult(ApplyFix(context.Document, root, invocation, argument, plan)),
                equivalenceKey: Title),
            diagnostic);
    }

    private readonly struct FixPlan
    {
        public FixPlan(string template, List<ExpressionSyntax> extracted, int insertIndex)
        {
            Template = template;
            Extracted = extracted;
            InsertIndex = insertIndex;
        }

        public string Template { get; }
        public List<ExpressionSyntax> Extracted { get; }
        public int InsertIndex { get; }
    }

    // Decides where the extracted values go and whether the rewrite is safe.
    //
    // Template values must directly follow the message argument - or, for the exception-aware
    // shapes (WriteError/WriteCritical/Log), directly follow the exception argument that sits
    // after the message. Inserting there (rather than appending) keeps any pre-existing trailing
    // arguments after the new ones, and preserves left-to-right evaluation order: the
    // interpolation expressions were evaluated before the other arguments originally, and still are.
    private static bool TryComputeFix(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax messageArgument,
        InterpolatedStringExpressionSyntax interpolated,
        out FixPlan plan,
        CancellationToken ct)
    {
        plan = default;

        // Multi-line raw strings strip indentation that ValueText does not reflect.
        string start = interpolated.StringStartToken.Text;
        if (start.IndexOf("\"\"\"", StringComparison.Ordinal) >= 0 && interpolated.ToString().IndexOf('\n') >= 0)
        {
            return false;
        }

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
        int msgIndex = args.IndexOf(messageArgument);
        if (msgIndex < 0)
        {
            return false;
        }

        // Named arguments make positional insertion order ambiguous; only the message itself
        // may be named, and only when everything before it is positional.
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].NameColon is not null && i != msgIndex)
            {
                return false;
            }
        }

        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        int insertIndex = msgIndex + 1;
        bool exceptionAware = method.Name is "WriteError" or "WriteCritical" or "Log" or "WriteErrorException" or "WriteCriticalException";
        if (exceptionAware && insertIndex < args.Count && IsExceptionTyped(model, args[insertIndex].Expression, ct))
        {
            insertIndex++;
        }

        // An explicit array in the trailing position is a params array; inserting before it would
        // change which argument the array binds to.
        for (int i = insertIndex; i < args.Count; i++)
        {
            ITypeSymbol? t = model.GetTypeInfo(args[i].Expression, ct).Type;
            if (t is IArrayTypeSymbol)
            {
                return false;
            }
        }

        bool isRaw = start.IndexOf("\"\"\"", StringComparison.Ordinal) >= 0;
        var template = new StringBuilder();
        var extracted = new List<ExpressionSyntax>();
        int fallbackIndex = 0;

        foreach (InterpolatedStringContentSyntax content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    // In regular and verbatim interpolated strings the token's ValueText keeps
                    // the "{{"/"}}" escapes exactly as the template needs them, so it is copied
                    // unchanged (re-escaping would double them - F-005). In a $$"""..."""
                    // raw string a single brace is literal text and must be doubled.
                    if (isRaw)
                    {
                        foreach (char c in text.TextToken.ValueText)
                        {
                            _ = c switch
                            {
                                '{' => template.Append("{{"),
                                '}' => template.Append("}}"),
                                _ => template.Append(c),
                            };
                        }
                    }
                    else
                    {
                        _ = template.Append(text.TextToken.ValueText);
                    }                    break;

                case InterpolationSyntax interpolation:
                    string name = DeriveName(interpolation.Expression, ref fallbackIndex);
                    _ = template.Append('{').Append(name);
                    if (interpolation.AlignmentClause is not null)
                    {
                        _ = template.Append(interpolation.AlignmentClause.ToString()); // ",10"
                    }
                    if (interpolation.FormatClause is not null)
                    {
                        _ = template.Append(interpolation.FormatClause.ToString());     // ":N2"
                    }
                    _ = template.Append('}');
                    extracted.Add(interpolation.Expression);
                    break;
            }
        }

        plan = new FixPlan(template.ToString(), extracted, insertIndex);
        return true;
    }

    private static bool IsExceptionTyped(SemanticModel model, ExpressionSyntax expression, CancellationToken ct)
    {
        for (ITypeSymbol? t = model.GetTypeInfo(expression, ct).Type; t is not null; t = t.BaseType)
        {
            if (t is { Name: "Exception", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
            {
                return true;
            }
        }

        return false;
    }

    private static Document ApplyFix(
        Document document,
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax messageArgument,
        FixPlan plan)
    {
        LiteralExpressionSyntax literal = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(plan.Template));

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments
            .Replace(messageArgument, messageArgument.WithExpression(literal));

        int at = plan.InsertIndex;
        foreach (ExpressionSyntax expr in plan.Extracted)
        {
            args = args.Insert(at++, SyntaxFactory.Argument(expr.WithoutTrivia()));
        }

        InvocationExpressionSyntax newInvocation =
            invocation.WithArgumentList(invocation.ArgumentList.WithArguments(args));

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }
    // A simple identifier or the last segment of a member access makes a meaningful structured
    // property name; anything else gets a generated, valid placeholder name.
    private static string DeriveName(ExpressionSyntax expression, ref int fallbackIndex)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => "Arg" + fallbackIndex++,
        };
    }
}
