// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.DurableTask.Analyzers.Helpers;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

public abstract class OrchestrationAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(context =>
        {
            var knownSymbols = new KnownTypeSymbols(context.Compilation);

            if (knownSymbols.OrchestrationTriggerAttribute == null || knownSymbols.FunctionAttribute == null)
            {
                // symbols not available in this compilation, skip analysis
                return;
            }

            OrchestrationAnalysisResult result = new();

            context.RegisterSyntaxNodeAction(ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                // Checks whether the declared method is an orchestration, if not, returns
                if (ctx.ContainingSymbol is not IMethodSymbol methodSymbol ||
                    !methodSymbol.ContainsAttributeInAnyMethodArguments(knownSymbols.OrchestrationTriggerAttribute) ||
                    !methodSymbol.TryGetSingleValueFromAttribute(knownSymbols.FunctionAttribute, out string functionName))
                {
                    return;
                }

                var orchestration = new OrchestrationMethod(functionName, methodSymbol);
                var methodSyntax = (MethodDeclarationSyntax)ctx.Node;

                FindInvokedMethods(ctx.SemanticModel, methodSyntax, methodSymbol, orchestration, result);
            }, SyntaxKind.MethodDeclaration);

            // allows concrete implementations to register specific actions/analysis and then check if they happen in any of the orchestration methods
            this.RegisterAdditionalCompilationStartAction(context, result);
        });
    }

    // Recursively find all methods invoked by the orchestration method
    static void FindInvokedMethods(
        SemanticModel semanticModel, MethodDeclarationSyntax callerSyntax, IMethodSymbol callerSymbol,
        OrchestrationMethod rootOrchestration, OrchestrationAnalysisResult result)
    {
        if (!TryTrackMethod(semanticModel, callerSyntax, callerSymbol, rootOrchestration, result))
        {
            // previously tracked method, leave to avoid infinite recursion
            return;
        }

        foreach (InvocationExpressionSyntax invocationSyntax in callerSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            IOperation? operation = semanticModel.GetOperation(invocationSyntax);
            if (operation == null || operation is not IInvocationOperation invocation)
            {
                continue;
            }

            IMethodSymbol calleeMethodSymbol = invocation.TargetMethod;
            if (calleeMethodSymbol == null)
            {
                continue;
            }

            // iterating over multiple syntax references is needed because the same method can be declared in multiple places (e.g. partial classes)
            IEnumerable<MethodDeclarationSyntax> calleeSyntaxes = calleeMethodSymbol.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>();
            foreach (MethodDeclarationSyntax calleeSyntax in calleeSyntaxes)
            {
                FindInvokedMethods(semanticModel, calleeSyntax, calleeMethodSymbol, rootOrchestration, result);
            }
        }
    }

    static bool TryTrackMethod(SemanticModel semanticModel, MethodDeclarationSyntax callerSyntax, IMethodSymbol callerSymbol,
        OrchestrationMethod rootOrchestration, OrchestrationAnalysisResult result)
    {
        ConcurrentBag<OrchestrationMethod> orchestrations = result.OrchestrationsByMethod.GetOrAdd(callerSymbol, []);
        if (orchestrations.Contains(rootOrchestration))
        {
            return false;
        }

        orchestrations.Add(rootOrchestration);

        return true;
    }

    /// <summary>
    /// Register additional actions to be executed after the compilation has started.
    /// It is expected from a concrete implementation of <see cref="OrchestrationAnalyzer"/> to register a
    /// <see cref="CompilationStartAnalysisContext.RegisterCompilationEndAction"/>
    /// and then compare that any discovered violations happened in any of the symbols in <paramref name="orchestrationAnalysisResult"/>.
    /// </summary>
    /// <param name="context">Context originally provided by <see cref="AnalysisContext.RegisterCompilationAction"/></param>
    /// <param name="orchestrationAnalysisResult">Collection of symbols referenced by orchestrations</param>
    protected abstract void RegisterAdditionalCompilationStartAction(CompilationStartAnalysisContext context, OrchestrationAnalysisResult orchestrationAnalysisResult);

    protected readonly struct OrchestrationAnalysisResult
    {
        public ConcurrentDictionary<IMethodSymbol, ConcurrentBag<OrchestrationMethod>> OrchestrationsByMethod { get; }

        public OrchestrationAnalysisResult()
        {
            this.OrchestrationsByMethod = new(SymbolEqualityComparer.Default);
        }
    }

    [DebuggerDisplay("[{FunctionName}] {OrchestrationMethodSymbol.Name}")]
    protected readonly struct OrchestrationMethod(string functionName, IMethodSymbol orchestrationMethodSymbol)
    {
        public string FunctionName { get; } = functionName;
        public IMethodSymbol OrchestrationMethodSymbol { get; } = orchestrationMethodSymbol;
    }
}
