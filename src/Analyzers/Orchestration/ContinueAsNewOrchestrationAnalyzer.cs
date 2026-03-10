// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.ContinueAsNewOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when an orchestration contains an unconditional while loop
/// that calls any TaskOrchestrationContext method (e.g. CallActivityAsync, WaitForExternalEvent,
/// CallSubOrchestratorAsync, CreateTimer) but no ContinueAsNew call within that loop.
/// Every orchestration API call adds to the replay history, so unbounded loops without
/// ContinueAsNew lead to unbounded history growth.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContinueAsNewOrchestrationAnalyzer : OrchestrationAnalyzer<ContinueAsNewOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0011";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.ContinueAsNewOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.ContinueAsNewOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://go.microsoft.com/fwlink/?linkid=2346202");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects orchestration methods for unbounded loops without ContinueAsNew.
    /// Only direct invocations within the loop body are considered; calls made through helper
    /// methods invoked from the loop are not tracked back to the loop context.
    /// </summary>
    public sealed class ContinueAsNewOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.TaskOrchestrationContext is not null;
        }

        /// <inheritdoc/>
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            IInvocationOperation[] allInvocations = methodOperation.Descendants().OfType<IInvocationOperation>().ToArray();

            foreach (WhileStatementSyntax whileStatement in methodSyntax.DescendantNodes().OfType<WhileStatementSyntax>())
            {
                if (!IsAlwaysTrueCondition(whileStatement.Condition))
                {
                    continue;
                }

                bool hasHistoryGrowingCall = false;
                bool hasContinueAsNew = false;

                foreach (IInvocationOperation invocation in allInvocations)
                {
                    if (!whileStatement.Span.Contains(invocation.Syntax.Span))
                    {
                        continue;
                    }

                    IMethodSymbol targetMethod = invocation.TargetMethod;

                    if (targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskOrchestrationContext, "ContinueAsNew"))
                    {
                        hasContinueAsNew = true;
                    }
                    else if (SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, this.KnownTypeSymbols.TaskOrchestrationContext) ||
                             SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType?.OriginalDefinition, this.KnownTypeSymbols.TaskOrchestrationContext))
                    {
                        hasHistoryGrowingCall = true;
                    }

                    if (hasHistoryGrowingCall && hasContinueAsNew)
                    {
                        break;
                    }
                }

                if (hasHistoryGrowingCall && !hasContinueAsNew)
                {
                    reportDiagnostic(Diagnostic.Create(Rule, whileStatement.WhileKeyword.GetLocation(), orchestrationName));
                }
            }
        }

        static bool IsAlwaysTrueCondition(ExpressionSyntax condition)
        {
            return condition is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.TrueLiteralExpression);
        }
    }
}
