// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when Guid.NewGuid() is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GuidOrchestrationAnalyzer : OrchestrationAnalyzer
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
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    protected override void RegisterAdditionalCompilationStartAction(CompilationStartAnalysisContext context, OrchestrationAnalysisResult orchestrationAnalysisResult)
    {
        var knownSymbols = new KnownTypeSymbols(context.Compilation);

        if (knownSymbols.GuidType == null)
        {
            return;
        }

        ConcurrentBag<(ISymbol Symbol, IInvocationOperation Invocation)> guidUsage = [];

        context.RegisterOperationAction(
            ctx =>
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();

            IInvocationOperation invocation = (IInvocationOperation)ctx.Operation;

            if (!invocation.TargetMethod.ContainingType.Equals(knownSymbols.GuidType, SymbolEqualityComparer.Default))
            {
                return;
            }

            if (invocation.TargetMethod.Name is nameof(Guid.NewGuid))
            {
                ISymbol method = ctx.ContainingSymbol;
                guidUsage.Add((method, invocation));
            }
        },
            OperationKind.Invocation);

        // compare whether the found Guid usages occur in methods invoked by orchestrations
        context.RegisterCompilationEndAction(ctx =>
        {
            foreach ((ISymbol symbol, IInvocationOperation operation) in guidUsage)
            {
                if (symbol is IMethodSymbol method)
                {
                    if (orchestrationAnalysisResult.OrchestrationsByMethod.TryGetValue(method, out ConcurrentBag<AnalyzedOrchestration> orchestrations))
                    {
                        string methodName = symbol.Name;
                        string invocationName = operation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                        string orchestrationNames = string.Join(", ", orchestrations.Select(o => o.Name).OrderBy(n => n));

                        // e.g.: "The method 'Method1' uses 'Guid.NewGuid()' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                        ctx.ReportDiagnostic(Rule, operation, methodName, invocationName, orchestrationNames);
                    }
                }
            }
        });
    }
}
