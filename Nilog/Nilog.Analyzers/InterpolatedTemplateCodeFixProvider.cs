// -----------------------------------------------------------------------------
//  Nilog.Analyzers — code fix for NILOG001. Rewrites an interpolated string used
//  as a Nilog message template into a literal '{Name}' template plus the
//  interpolated expressions appended as separate arguments, e.g.
//
//      logger.WriteInformation($"User {id} from {ip}");
//  ->  logger.WriteInformation("User {id} from {ip}", id, ip);
//
//  Appending the extracted values to the END of the argument list is correct for
//  every Nilog call shape, because the template values are always the trailing
//  parameters (after message, and after any exception).
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

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => Task.FromResult(
                    ApplyFix(context.Document, root, invocation, argument, interpolated)),
                equivalenceKey: Title),
            diagnostic);
    }

    private static Document ApplyFix(
        Document document,
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax messageArgument,
        InterpolatedStringExpressionSyntax interpolated)
    {
        var template = new StringBuilder();
        var extracted = new List<ExpressionSyntax>();
        int fallbackIndex = 0;

        foreach (InterpolatedStringContentSyntax content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    // ValueText has "{{"/"}}" already collapsed to "{"/"}"; re-escape so the
                    // literal template renders the same braces at runtime.
                    foreach (char c in text.TextToken.ValueText)
                    {
                        _ = c switch
                        {
                            '{' => template.Append("{{"),
                            '}' => template.Append("}}"),
                            _ => template.Append(c),
                        };
                    }
                    break;

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

        LiteralExpressionSyntax literal = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(template.ToString()));

        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments
            .Replace(messageArgument, messageArgument.WithExpression(literal));

        foreach (ExpressionSyntax expr in extracted)
        {
            args = args.Add(SyntaxFactory.Argument(expr.WithoutTrivia()));
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
