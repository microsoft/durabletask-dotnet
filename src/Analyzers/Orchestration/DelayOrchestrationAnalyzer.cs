// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when Task.Delay or Thread.Sleep is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DelayOrchestrationAnalyzer : OrchestrationAnalyzer
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
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    protected override void RegisterAdditionalCompilationStartAction(CompilationStartAnalysisContext context, OrchestrationAnalysisResult orchestrationAnalysisResult)
    {
        var knownSymbols = new KnownTypeSymbols(context.Compilation);

        if (knownSymbols.Thread == null || knownSymbols.Task == null || knownSymbols.TaskT == null)
        {
            return;
        }

        ConcurrentBag<(ISymbol Symbol, IInvocationOperation Invocation)> delayUsage = [];

        context.RegisterOperationAction(
            ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                IInvocationOperation invocation = (IInvocationOperation)ctx.Operation;

                if (!invocation.TargetMethod.ContainingType.Equals(knownSymbols.Thread, SymbolEqualityComparer.Default))
                {
                    return;
                }

                if (invocation.TargetMethod.Name is nameof(Thread.Sleep))
                {
                    ISymbol method = ctx.ContainingSymbol;
                    delayUsage.Add((method, invocation));
                }
            },
            OperationKind.Invocation);

        context.RegisterOperationAction(
            ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                IInvocationOperation invocation = (IInvocationOperation)ctx.Operation;

                if (!invocation.TargetMethod.ContainingType.Equals(knownSymbols.Task, SymbolEqualityComparer.Default) &&
                    !invocation.TargetMethod.ContainingType.Equals(knownSymbols.TaskT, SymbolEqualityComparer.Default))
                {
                    return;
                }

                if (invocation.TargetMethod.Name is nameof(Task.Delay))
                {
                    ISymbol method = ctx.ContainingSymbol;
                    delayUsage.Add((method, invocation));
                }
            },
            OperationKind.Invocation);

        context.RegisterCompilationEndAction(ctx =>
        {
            foreach ((ISymbol symbol, IInvocationOperation operation) in delayUsage)
            {
                if (symbol is IMethodSymbol method)
                {
                    if (orchestrationAnalysisResult.OrchestrationsByMethod.TryGetValue(method, out ConcurrentBag<AnalyzedOrchestration> orchestrations))
                    {
                        string methodName = symbol.Name;
                        string invocationName = operation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
                        string orchestrationNames = string.Join(", ", orchestrations.Select(o => o.Name).OrderBy(n => n));

                        // e.g.: "The method 'Method1' uses 'Thread.Sleep(int)' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                        ctx.ReportDiagnostic(Rule, operation, methodName, invocationName, orchestrationNames);
                    }
                }
            }
        });
    }
}
