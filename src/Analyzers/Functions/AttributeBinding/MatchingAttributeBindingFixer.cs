// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;

/// <summary>
/// Base class for code fixers that fix the type of a parameter to match the expected type.
/// </summary>
public abstract class MatchingAttributeBindingFixer : CodeFixProvider
{
    /// <summary>
    /// Gets the expected type to be used during the code fix.
    /// </summary>
    public abstract string ExpectedType { get; }

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null)
        {
            return;
        }

        // Find the parameter syntax node that is causing the diagnostic.
        if (root.FindNode(context.Span) is not ParameterSyntax parameterSyntax)
        {
            return;
        }

        TypeSyntax? incorrectTypeSyntax = parameterSyntax.Type;
        if (incorrectTypeSyntax == null)
        {
            return;
        }

        // e.g: "Use 'TaskOrchestrationContext' instead of 'string'"
        string title = string.Format(
            CultureInfo.InvariantCulture,
            Resources.MatchingAttributeBindingFixerTitle,
            this.ExpectedType,
            incorrectTypeSyntax.ToString());

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: _ => ReplaceMismatchedType(context.Document, root, incorrectTypeSyntax, this.ExpectedType),
                equivalenceKey: title), // This key is used to prevent duplicate code fixes.
            context.Diagnostics);
    }

    static Task<Document> ReplaceMismatchedType(Document document, SyntaxNode oldRoot, TypeSyntax incorrectTypeSyntax, string expectedType)
    {
        // Create the correct type syntax by using the identifier name provided by the derived class.
        TypeSyntax correctTypeSyntax = SyntaxFactory.IdentifierName(expectedType);

        // Replace the old local declaration with the new local declaration.
        SyntaxNode newRoot = oldRoot.ReplaceNode(incorrectTypeSyntax, correctTypeSyntax);
        Document newDocument = document.WithSyntaxRoot(newRoot);

        return Task.FromResult(newDocument);
    }
}
