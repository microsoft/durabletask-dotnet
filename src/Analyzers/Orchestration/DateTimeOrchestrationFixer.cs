// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
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
        // Gets the name of the TaskOrchestrationContext parameter (e.g. "context" or "ctx")
        string contextParameterName = orchestrationContext.TaskOrchestrationContextSymbol.Name;
        SemanticModel semanticModel = orchestrationContext.SemanticModel;

        // Handle DateTime/DateTimeOffset property access (e.g. DateTime.Now or DateTimeOffset.Now)
        if (orchestrationContext.SyntaxNodeWithDiagnostic is MemberAccessExpressionSyntax dateTimeExpression)
        {
            // Use semantic analysis to determine if this is a DateTimeOffset expression
            ITypeSymbol? typeSymbol = semanticModel.GetTypeInfo(dateTimeExpression.Expression).Type;
            bool isDateTimeOffset = typeSymbol?.ToDisplayString() == "System.DateTimeOffset";

            bool isDateTimeToday = dateTimeExpression.Name.ToString() == "Today";

            // Build the recommendation text
            string recommendation;
            if (isDateTimeOffset)
            {
                // For DateTimeOffset, we always just cast CurrentUtcDateTime
                recommendation = $"(DateTimeOffset){contextParameterName}.CurrentUtcDateTime";
            }
            else
            {
                // For DateTime, we may need to add .Date for Today
                string dateTimeTodaySuffix = isDateTimeToday ? ".Date" : string.Empty;
                recommendation = $"{contextParameterName}.CurrentUtcDateTime{dateTimeTodaySuffix}";
            }

            // e.g: "Use 'context.CurrentUtcDateTime' instead of 'DateTime.Now'"
            // e.g: "Use 'context.CurrentUtcDateTime.Date' instead of 'DateTime.Today'"
            // e.g: "Use '(DateTimeOffset)context.CurrentUtcDateTime' instead of 'DateTimeOffset.Now'"
            string title = string.Format(
                CultureInfo.InvariantCulture,
                Resources.UseInsteadFixerTitle,
                recommendation,
                dateTimeExpression);

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ReplaceDateTime(context.Document, orchestrationContext.Root, dateTimeExpression, contextParameterName, isDateTimeToday, isDateTimeOffset),
                    equivalenceKey: title), // This key is used to prevent duplicate code fixes.
                context.Diagnostics);
            return;
        }

        // Handle TimeProvider method invocations (e.g. TimeProvider.System.GetUtcNow())
        // The node might be the invocation itself or a child node, so we need to find the InvocationExpressionSyntax
        InvocationExpressionSyntax? timeProviderInvocation = orchestrationContext.SyntaxNodeWithDiagnostic as InvocationExpressionSyntax
            ?? orchestrationContext.SyntaxNodeWithDiagnostic.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (timeProviderInvocation != null &&
            semanticModel.GetSymbolInfo(timeProviderInvocation).Symbol is IMethodSymbol methodSymbol)
        {
            string methodName = methodSymbol.Name;

            // Check if the method returns DateTimeOffset
            bool returnsDateTimeOffset = methodSymbol.ReturnType.ToDisplayString() == "System.DateTimeOffset";

            // Build the recommendation based on the method name
            string recommendation = methodName switch
            {
                "GetUtcNow" when returnsDateTimeOffset => $"(DateTimeOffset){contextParameterName}.CurrentUtcDateTime",
                "GetUtcNow" => $"{contextParameterName}.CurrentUtcDateTime",
                "GetLocalNow" when returnsDateTimeOffset => $"(DateTimeOffset){contextParameterName}.CurrentUtcDateTime.ToLocalTime()",
                "GetLocalNow" => $"{contextParameterName}.CurrentUtcDateTime.ToLocalTime()",
                "GetTimestamp" => $"{contextParameterName}.CurrentUtcDateTime.Ticks",
                _ => $"{contextParameterName}.CurrentUtcDateTime",
            };

            // e.g: "Use 'context.CurrentUtcDateTime' instead of 'TimeProvider.System.GetUtcNow()'"
            string title = string.Format(
                CultureInfo.InvariantCulture,
                Resources.UseInsteadFixerTitle,
                recommendation,
                timeProviderInvocation);

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ReplaceTimeProvider(context.Document, orchestrationContext.Root, timeProviderInvocation, contextParameterName, methodName, returnsDateTimeOffset),
                    equivalenceKey: title),
                context.Diagnostics);
        }
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

    static Task<Document> ReplaceTimeProvider(Document document, SyntaxNode oldRoot, InvocationExpressionSyntax incorrectTimeProviderSyntax, string contextParameterName, string methodName, bool returnsDateTimeOffset)
    {
        // Build the correct expression based on the method name
        ExpressionSyntax correctExpression = methodName switch
        {
            "GetUtcNow" => MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(contextParameterName),
                IdentifierName("CurrentUtcDateTime")),
            "GetLocalNow" => InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(contextParameterName),
                        IdentifierName("CurrentUtcDateTime")),
                    IdentifierName("ToLocalTime"))),
            "GetTimestamp" => MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(contextParameterName),
                    IdentifierName("CurrentUtcDateTime")),
                IdentifierName("Ticks")),
            _ => MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName(contextParameterName),
                IdentifierName("CurrentUtcDateTime")),
        };

        // If the method returns DateTimeOffset, we need to cast the DateTime to DateTimeOffset
        if (returnsDateTimeOffset)
        {
            correctExpression = CastExpression(
                IdentifierName("DateTimeOffset"),
                correctExpression);
        }

        // Replaces the old invocation with the new expression
        SyntaxNode newRoot = oldRoot.ReplaceNode(incorrectTimeProviderSyntax, correctExpression);
        Document newDocument = document.WithSyntaxRoot(newRoot);

        return Task.FromResult(newDocument);
    }
}
