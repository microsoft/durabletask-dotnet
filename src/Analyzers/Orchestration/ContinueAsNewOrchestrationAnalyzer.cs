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
/// with WaitForExternalEvent or CallSubOrchestratorAsync but no reachable ContinueAsNew call.
/// This pattern can lead to unbounded history growth.
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
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects orchestration methods for unbounded loops without ContinueAsNew.
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

            foreach (WhileStatementSyntax whileStatement in methodSyntax.DescendantNodes().OfType<WhileStatementSyntax>())
            {
                if (!IsAlwaysTrueCondition(whileStatement.Condition))
                {
                    continue;
                }

                bool hasHistoryGrowingCall = false;
                bool hasContinueAsNew = false;

                foreach (IInvocationOperation invocation in methodOperation.Descendants().OfType<IInvocationOperation>())
                {
                    if (!whileStatement.Span.Contains(invocation.Syntax.Span))
                    {
                        continue;
                    }

                    IMethodSymbol targetMethod = invocation.TargetMethod;

                    if (targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskOrchestrationContext, "WaitForExternalEvent") ||
                        targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskOrchestrationContext, "CallSubOrchestratorAsync"))
                    {
                        hasHistoryGrowingCall = true;
                    }

                    if (targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskOrchestrationContext, "ContinueAsNew"))
                    {
                        hasContinueAsNew = true;
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
