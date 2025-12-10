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
/// Code fix provider for the <see cref="DateTimeOrchestrationAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeOrchestrationFixer))]
[Shared]
public sealed class DateTimeOrchestrationFixer : OrchestrationContextFixer
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [DateTimeOrchestrationAnalyzer.DiagnosticId];

    /// <inheritdoc/>
    protected override void RegisterCodeFixes(CodeFixContext context, OrchestrationCodeFixContext orchestrationContext)
    {
        // Parses the syntax node to see if it is a member access expression (e.g. DateTime.Now or DateTimeOffset.Now)
        if (orchestrationContext.SyntaxNodeWithDiagnostic is not MemberAccessExpressionSyntax dateTimeExpression)
        {
            return;
        }

        // Gets the name of the TaskOrchestrationContext parameter (e.g. "context" or "ctx")
        string contextParameterName = orchestrationContext.TaskOrchestrationContextSymbol.Name;

        // Check if this is a DateTimeOffset expression
        bool isDateTimeOffset = dateTimeExpression.Expression.ToString().Contains("DateTimeOffset");
        bool isDateTimeToday = dateTimeExpression.Name.ToString() == "Today";
        string dateTimeTodaySuffix = isDateTimeToday ? ".Date" : string.Empty;
        string recommendation = $"{contextParameterName}.CurrentUtcDateTime{dateTimeTodaySuffix}";

        // e.g: "Use 'context.CurrentUtcDateTime' instead of 'DateTime.Now'"
        // e.g: "Use 'context.CurrentUtcDateTime.Date' instead of 'DateTime.Today'"
        // e.g: "Use 'context.CurrentUtcDateTime' instead of 'DateTimeOffset.Now'"
        string title = string.Format(
            CultureInfo.InvariantCulture,
            Resources.UseInsteadFixerTitle,
            recommendation,
            dateTimeExpression.ToString());

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: c => ReplaceDateTime(context.Document, orchestrationContext.Root, dateTimeExpression, contextParameterName, isDateTimeToday, isDateTimeOffset),
                equivalenceKey: title), // This key is used to prevent duplicate code fixes.
            context.Diagnostics);
    }

    static Task<Document> ReplaceDateTime(Document document, SyntaxNode oldRoot, MemberAccessExpressionSyntax incorrectDateTimeSyntax, string contextParameterName, bool isDateTimeToday, bool isDateTimeOffset)
    {
        // Builds a 'context.CurrentUtcDateTime' syntax node
        ExpressionSyntax correctDateTimeSyntax =
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(contextParameterName),
                IdentifierName("CurrentUtcDateTime"));

        // If the original expression was DateTime.Today, we add ".Date" to the context expression.
        if (isDateTimeToday)
        {
            correctDateTimeSyntax = MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                correctDateTimeSyntax,
                IdentifierName("Date"));
        }

        // If the original expression was DateTimeOffset, we need to cast the DateTime to DateTimeOffset
        // This is done using a CastExpression: (DateTimeOffset)context.CurrentUtcDateTime
        if (isDateTimeOffset)
        {
            correctDateTimeSyntax = CastExpression(
                IdentifierName("DateTimeOffset"),
                correctDateTimeSyntax);
        }

        // Replaces the old local declaration with the new local declaration.
        SyntaxNode newRoot = oldRoot.ReplaceNode(incorrectDateTimeSyntax, correctDateTimeSyntax);
        Document newDocument = document.WithSyntaxRoot(newRoot);

        return Task.FromResult(newDocument);
    }
}
