// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.GuidOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when Guid.NewGuid() is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GuidOrchestrationAnalyzer : OrchestrationAnalyzer<GuidOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    internal const string DiagnosticId = "DURABLE0002";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.GuidOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.GuidOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

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
    /// Visitor that inspects the method body for Guid.NewGuid() calls.
    /// </summary>
    public sealed class GuidOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.GuidType is not null;
        }

        /// <inheritdoc/>
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            foreach (IInvocationOperation invocation in methodOperation.Descendants().OfType<IInvocationOperation>())
            {
                IMethodSymbol targetMethod = invocation.TargetMethod;

                if (targetMethod.IsEqualTo(this.KnownTypeSymbols.GuidType, nameof(Guid.NewGuid)))
                {
                    string invocationName = targetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

                    // e.g.: "The method 'Method1' uses 'Guid.NewGuid()' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, invocation, methodSymbol.Name, invocationName, orchestrationName));
                }
            }
        }
    }
}
