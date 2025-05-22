// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Dapr.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Code fix provider for the <see cref="DelayOrchestrationAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DelayOrchestrationFixer))]
[Shared]
public sealed class DelayOrchestrationFixer : OrchestrationContextFixer
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [DelayOrchestrationAnalyzer.DiagnosticId];

    /// <inheritdoc/>
    protected override void RegisterCodeFixes(CodeFixContext context, OrchestrationCodeFixContext orchestrationContext)
    {
        if (orchestrationContext.SyntaxNodeWithDiagnostic is not InvocationExpressionSyntax invocationExpressionsSyntax)
        {
            return;
        }

        if (orchestrationContext.SemanticModel.GetOperation(invocationExpressionsSyntax) is not IInvocationOperation invocationOperation)
        {
            return;
        }

        // Only fix Task.Delay(int[,CancellationToken]) or Task.Delay(TimeSpan[,CancellationToken]) invocations.
        // For now, fixing Thread.Sleep(int) is not supported
        if (!SymbolEqualityComparer.Default.Equals(invocationOperation.Type, orchestrationContext.KnownTypeSymbols.Task))
        {
            return;
        }

        Compilation compilation = orchestrationContext.SemanticModel.Compilation;
        INamedTypeSymbol int32 = compilation.GetSpecialType(SpecialType.System_Int32);

        // Extracts the arguments from the Task.Delay invocation
        IMethodSymbol taskDelaySymbol = invocationOperation.TargetMethod;
        Debug.Assert(taskDelaySymbol.Parameters.Length >= 1, "Task.Delay should have at least one parameter");
        bool isInt = SymbolEqualityComparer.Default.Equals(taskDelaySymbol.Parameters[0].Type, int32);
        IArgumentOperation delayArgumentOperation = invocationOperation.Arguments[0];
        IArgumentOperation? cancellationTokenArgumentOperation = invocationOperation.Arguments.Length == 2 ? invocationOperation.Arguments[1] : null;

        // Gets the name of the TaskOrchestrationContext parameter (e.g. "context" or "ctx")
        string contextParameterName = orchestrationContext.TaskOrchestrationContextSymbol.Name;
        string recommendation = $"{contextParameterName}.CreateTimer";

        // e.g: "Use 'context.CreateTimer' instead of 'Task.Delay'"
        string title = string.Format(
            CultureInfo.InvariantCulture,
            Resources.UseInsteadFixerTitle,
            recommendation,
            "Task.Delay");

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: c => ReplaceTaskDelay(
                    context.Document, orchestrationContext.Root, invocationExpressionsSyntax, contextParameterName, delayArgumentOperation, cancellationTokenArgumentOperation, isInt),
                equivalenceKey: title), // This key is used to prevent duplicate code fixes.
            context.Diagnostics);
    }

    static Task<Document> ReplaceTaskDelay(
        Document document,
        SyntaxNode oldRoot,
        InvocationExpressionSyntax incorrectTaskDelaySyntax,
        string contextParameterName,
        IArgumentOperation delayArgumentOperation,
        IArgumentOperation? cancellationTokenArgumentOperation,
        bool isInt)
    {
        if (delayArgumentOperation.Syntax is not ArgumentSyntax timeSpanOrIntArgumentSyntax)
        {
            return Task.FromResult(document);
        }

        // Either use the original TimeSpan argument, or in case it is an int, transform it into TimeSpan
        ArgumentSyntax timeSpanArgumentSyntax;
        if (isInt)
        {
            timeSpanArgumentSyntax =
                Argument(
                    InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("TimeSpan"),
                            IdentifierName("FromMilliseconds")),
                        ArgumentList(
                            SeparatedList(new[] { timeSpanOrIntArgumentSyntax }))));
        }
        else
        {
            timeSpanArgumentSyntax = timeSpanOrIntArgumentSyntax;
        }

        // Either gets the original cancellation token argument or create a 'CancellationToken.None'
        ArgumentSyntax cancellationTokenArgumentSyntax = cancellationTokenArgumentOperation?.Syntax as ArgumentSyntax ??
            Argument(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("CancellationToken"),
                    IdentifierName("None")));

        // Builds a 'context.CreateTimer(TimeSpan.FromMilliseconds(1000), CancellationToken.None)' syntax node
        InvocationExpressionSyntax correctTimerSyntax =
            InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(contextParameterName),
                    IdentifierName("CreateTimer")),
                ArgumentList(
                    SeparatedList(new[]
                    {
                        timeSpanArgumentSyntax,
                        cancellationTokenArgumentSyntax,
                    })));

        // Replaces the old local declaration with the new local declaration.
        SyntaxNode newRoot = oldRoot.ReplaceNode(incorrectTaskDelaySyntax, correctTimerSyntax);
        Document newDocument = document.WithSyntaxRoot(newRoot);

        return Task.FromResult(newDocument);
    }
}
