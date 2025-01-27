// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Code fix provider for the <see cref="GuidOrchestrationAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GuidOrchestrationFixer))]
[Shared]
public sealed class GuidOrchestrationFixer : OrchestrationContextFixer
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [GuidOrchestrationAnalyzer.DiagnosticId];

    /// <inheritdoc/>
    protected override void RegisterCodeFixes(CodeFixContext context, OrchestrationCodeFixContext orchestrationContext)
    {
        // Parses the syntax node to see if it is a invocation expression (Guid.NewGuid())
        if (orchestrationContext.SyntaxNodeWithDiagnostic is not InvocationExpressionSyntax guidExpression)
        {
            return;
        }

        // Gets the name of the TaskOrchestrationContext parameter (e.g. "context" or "ctx")
        string contextParameterName = orchestrationContext.TaskOrchestrationContextSymbol.Name;

        string recommendation = $"{contextParameterName}.NewGuid()";

        // e.g: "Use 'context.NewGuid()' instead of 'Guid.NewGuid()'"
        string title = string.Format(
            CultureInfo.InvariantCulture,
            Resources.UseInsteadFixerTitle,
            recommendation,
            guidExpression.ToString());

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: c => ReplaceGuid(context.Document, orchestrationContext.Root, guidExpression, contextParameterName),
                equivalenceKey: title), // This key is used to prevent duplicate code fixes.
            context.Diagnostics);
    }

    static Task<Document> ReplaceGuid(Document document, SyntaxNode oldRoot, InvocationExpressionSyntax incorrectGuidSyntax, string contextParameterName)
    {
        // Builds a 'context.NewGuid()' syntax node
        InvocationExpressionSyntax correctGuidSyntax =
            InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(contextParameterName),
                    IdentifierName("NewGuid")),
                ArgumentList());

        // Replaces the old local declaration with the new local declaration.
        SyntaxNode newRoot = oldRoot.ReplaceNode(incorrectGuidSyntax, correctGuidSyntax);
        Document newDocument = document.WithSyntaxRoot(newRoot);

        return Task.FromResult(newDocument);
    }
}
