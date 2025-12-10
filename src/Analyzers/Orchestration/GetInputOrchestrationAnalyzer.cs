// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.GetInputOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports an informational diagnostic when ctx.GetInput() is used in an orchestration method,
/// suggesting the use of input parameter binding instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GetInputOrchestrationAnalyzer : OrchestrationAnalyzer<GetInputOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0009";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.GetInputOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.GetInputOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects the method body for GetInput calls.
    /// </summary>
    public sealed class GetInputOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            foreach (IInvocationOperation operation in methodOperation.Descendants().OfType<IInvocationOperation>())
            {
                IMethodSymbol method = operation.TargetMethod;

                // Check if this is a call to GetInput<T>() on TaskOrchestrationContext
                if (method.Name != "GetInput" || !method.IsGenericMethod)
                {
                    continue;
                }

                // Verify the containing type is TaskOrchestrationContext
                if (!method.ContainingType.Equals(this.KnownTypeSymbols.TaskOrchestrationContext, SymbolEqualityComparer.Default))
                {
                    continue;
                }

                // e.g.: "Consider using an input parameter instead of 'GetInput<T>()' in orchestration 'MyOrchestrator'"
                reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, operation.Syntax, orchestrationName));
            }
        }
    }
}
