// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.EnvironmentOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

#pragma warning disable RS1035 // Environment Variables are not supposed to be used in Analyzers, but here we just reference the API, never using it.

/// <summary>
/// Analyzer that reports usage of <see cref="System.Environment"/> APIs in orchestrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnvironmentOrchestrationAnalyzer : OrchestrationAnalyzer<EnvironmentOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0006";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.EnvironmentOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.EnvironmentOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: $"https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects the method body for retrievals of Environment Variables through the <see cref="System.Environment" /> type.
    /// </summary>
    public sealed class EnvironmentOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.Environment != null;
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

                if (targetMethod.ContainingType.Equals(this.KnownTypeSymbols.Environment, SymbolEqualityComparer.Default) &&
                    targetMethod.Name is nameof(Environment.GetEnvironmentVariable) or nameof(Environment.GetEnvironmentVariables) or nameof(Environment.ExpandEnvironmentVariables))
                {
                    string invocationName = targetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

                    // e.g.: "The method 'Method1' uses environment variables through 'Environment.GetEnvironmentVariable()' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, invocation, methodSymbol.Name, invocationName, orchestrationName));
                }
            }
        }
    }
}
