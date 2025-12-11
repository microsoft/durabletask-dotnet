// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.DelayOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when Task.Delay or Thread.Sleep is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DelayOrchestrationAnalyzer : OrchestrationAnalyzer<DelayOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0003";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.DelayOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.DelayOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

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
    /// Visitor that inspects the method body for Task.Delay or Thread.Sleep calls.
    /// </summary>
    public sealed class DelayOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.Thread != null && this.KnownTypeSymbols.Task != null && this.KnownTypeSymbols.TaskT != null;
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

                if (targetMethod.IsEqualTo(this.KnownTypeSymbols.Thread, nameof(Thread.Sleep)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.Task, nameof(Task.Delay)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskT, nameof(Task.Delay)))
                {
                    string invocationName = targetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

                    // e.g.: "The method 'Method1' uses 'Thread.Sleep' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, invocation, methodSymbol.Name, invocationName, orchestrationName));
                }
            }
        }
    }
}
