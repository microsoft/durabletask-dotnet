// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Base class for analyzers that analyze orchestrations.
/// </summary>
public abstract class OrchestrationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // this analyzer uses concurrent collections/operations, so we can enable actions concurrent execution to improve performance
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(context =>
        {
            KnownTypeSymbols knownSymbols = new(context.Compilation);

            if (knownSymbols.FunctionOrchestrationAttribute == null || knownSymbols.FunctionNameAttribute == null ||
                knownSymbols.TaskOrchestratorInterface == null || knownSymbols.TaskOrchestratorBaseClass == null ||
                knownSymbols.DurableTaskRegistry == null)
            {
                // symbols not available in this compilation, skip analysis
                return;
            }

            IMethodSymbol? runAsyncTaskOrchestratorInterface = knownSymbols.TaskOrchestratorInterface.GetMembers("RunAsync").OfType<IMethodSymbol>().FirstOrDefault();
            IMethodSymbol? runAsyncTaskOrchestratorBase = knownSymbols.TaskOrchestratorBaseClass.GetMembers("RunAsync").OfType<IMethodSymbol>().FirstOrDefault();
            if (runAsyncTaskOrchestratorInterface == null || runAsyncTaskOrchestratorBase == null)
            {
                return;
            }

            OrchestrationAnalysisResult result = new();

            // look for Durable Functions Orchestrations
            context.RegisterSyntaxNodeAction(
                ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                if (ctx.ContainingSymbol is not IMethodSymbol methodSymbol)
                {
                    return;
                }

                if (!methodSymbol.ContainsAttributeInAnyMethodArguments(knownSymbols.FunctionOrchestrationAttribute))
                {
                    return;
                }

                if (!methodSymbol.TryGetSingleValueFromAttribute(knownSymbols.FunctionNameAttribute, out string functionName))
                {
                    return;
                }

                AnalyzedOrchestration orchestration = new(functionName);
                var rootMethodSyntax = (MethodDeclarationSyntax)ctx.Node;

                FindInvokedMethods(ctx.SemanticModel, rootMethodSyntax, methodSymbol, orchestration, result);
            },
                SyntaxKind.MethodDeclaration);

            // look for TaskOrchestrator`2 Orchestrations
            context.RegisterSyntaxNodeAction(
                ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                if (ctx.ContainingSymbol is not INamedTypeSymbol classSymbol)
                {
                    return;
                }

                if (!classSymbol.BaseTypeIsConstructedFrom(knownSymbols.TaskOrchestratorBaseClass))
                {
                    return;
                }

                // Get the method that overrides TaskOrchestrator.RunAsync
                IMethodSymbol? methodSymbol = classSymbol.GetOverridenMethod(runAsyncTaskOrchestratorBase);
                if (methodSymbol == null)
                {
                    return;
                }

                AnalyzedOrchestration orchestration = new(classSymbol.Name);

                IEnumerable<MethodDeclarationSyntax> methodSyntaxes = methodSymbol.GetSyntaxNodes();
                foreach (MethodDeclarationSyntax rootMethodSyntax in methodSyntaxes)
                {
                    FindInvokedMethods(ctx.SemanticModel, rootMethodSyntax, methodSymbol, orchestration, result);
                }
            },
                SyntaxKind.ClassDeclaration);

            // look for ITaskOrchestrator Orchestrations
            context.RegisterSyntaxNodeAction(
                ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                if (ctx.ContainingSymbol is not INamedTypeSymbol classSymbol)
                {
                    return;
                }

                // Gets the method that implements ITaskOrchestrator.RunAsync
                if (classSymbol.FindImplementationForInterfaceMember(runAsyncTaskOrchestratorInterface) is not IMethodSymbol methodSymbol)
                {
                    return;
                }

                // Skip if the found method is implemented in TaskOrchestrator<TInput, TOutput>
                if (methodSymbol.ContainingType.ConstructedFrom.Equals(knownSymbols.TaskOrchestratorBaseClass, SymbolEqualityComparer.Default))
                {
                    return;
                }

                AnalyzedOrchestration orchestration = new(classSymbol.Name);

                IEnumerable<MethodDeclarationSyntax> methodSyntaxes = methodSymbol.GetSyntaxNodes();
                foreach (MethodDeclarationSyntax rootMethodSyntax in methodSyntaxes)
                {
                    FindInvokedMethods(ctx.SemanticModel, rootMethodSyntax, methodSymbol, orchestration, result);
                }
            },
                SyntaxKind.ClassDeclaration);

            // look for OrchestratorFunc Orchestrations
            context.RegisterOperationAction(
                ctx =>
            {
                if (ctx.Operation is not IInvocationOperation invocation)
                {
                    return;
                }

                if (!SymbolEqualityComparer.Default.Equals(invocation.Type, knownSymbols.DurableTaskRegistry))
                {
                    return;
                }

                // there are 8 AddOrchestratorFunc overloads
                if (invocation.TargetMethod.Name != "AddOrchestratorFunc")
                {
                    return;
                }

                // all overloads have the parameter 'orchestrator', either as an Action or a Func
                IArgumentOperation orchestratorArgument = invocation.Arguments.First(a => a.Parameter!.Name == "orchestrator");
                if (orchestratorArgument.Value is not IDelegateCreationOperation delegateCreationOperation)
                {
                    return;
                }

                // obtains the method symbol from the delegate creation operation
                IMethodSymbol? methodSymbol = null;
                switch (delegateCreationOperation.Target)
                {
                    case IAnonymousFunctionOperation lambdaOperation:
                        // use the containing symbol of the lambda (e.g. the class declaring it) as the method symbol
                        methodSymbol = ctx.ContainingSymbol as IMethodSymbol;
                        break;
                    case IMethodReferenceOperation methodReferenceOperation:
                        // use the method reference as the method symbol
                        methodSymbol = methodReferenceOperation.Method;
                        break;
                    default:
                        break;
                }

                if (methodSymbol == null)
                {
                    return;
                }

                // try to get the name of the orchestration from the method call, otherwise use the containing type name
                IArgumentOperation nameArgument = invocation.Arguments.First(a => a.Parameter!.Name == "name");
                Optional<object?> name = nameArgument.GetConstantValueFromAttribute(ctx.Operation.SemanticModel!, ctx.CancellationToken);
                string orchestrationName = name.Value?.ToString() ?? methodSymbol.Name;

                AnalyzedOrchestration orchestration = new(orchestrationName);

                SyntaxNode funcRootSyntax = delegateCreationOperation.Syntax;

                FindInvokedMethods(ctx.Operation.SemanticModel!, funcRootSyntax, methodSymbol, orchestration, result);
            },
                OperationKind.Invocation);

            // allows concrete implementations to register specific actions/analysis and then check if they happen in any of the orchestration methods
            this.RegisterAdditionalCompilationStartAction(context, result);
        });
    }

    /// <summary>
    /// Register additional actions to be executed after the compilation has started.
    /// It is expected from a concrete implementation of <see cref="OrchestrationAnalyzer"/> to register a
    /// <see cref="CompilationStartAnalysisContext.RegisterCompilationEndAction"/>
    /// and then compare that to any discovered violations happened in any of the symbols in <paramref name="orchestrationAnalysisResult"/>.
    /// </summary>
    /// <param name="context">Context originally provided by <see cref="AnalysisContext.RegisterCompilationAction"/>.</param>
    /// <param name="orchestrationAnalysisResult">Collection of symbols referenced by orchestrations.</param>
    protected abstract void RegisterAdditionalCompilationStartAction(CompilationStartAnalysisContext context, OrchestrationAnalysisResult orchestrationAnalysisResult);

    // Recursively find all methods invoked by the orchestration root method and call the appropriate visitor method
    static void FindInvokedMethods(
        SemanticModel semanticModel,
        SyntaxNode callerSyntax,
        IMethodSymbol callerSymbol,
        AnalyzedOrchestration rootOrchestration,
        OrchestrationAnalysisResult result)
    {
        // add the visited method to the list of orchestrations
        ConcurrentBag<AnalyzedOrchestration> orchestrations = result.OrchestrationsByMethod.GetOrAdd(callerSymbol, []);
        if (orchestrations.Contains(rootOrchestration))
        {
            // previously tracked method, leave to avoid infinite recursion
            return;
        }

        orchestrations.Add(rootOrchestration);

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
            IEnumerable<MethodDeclarationSyntax> calleeSyntaxes = calleeMethodSymbol.GetSyntaxNodes();
            foreach (MethodDeclarationSyntax calleeSyntax in calleeSyntaxes)
            {
                FindInvokedMethods(semanticModel, calleeSyntax, calleeMethodSymbol, rootOrchestration, result);
            }
        }
    }

    /// <summary>
    /// Data structure to store the result of the orchestration methods analysis.
    /// </summary>
    protected readonly struct OrchestrationAnalysisResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrchestrationAnalysisResult"/> struct.
        /// </summary>
        public OrchestrationAnalysisResult()
        {
            this.OrchestrationsByMethod = new(SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// Gets the orchestrations that invokes/reaches a given method.
        /// </summary>
        public ConcurrentDictionary<IMethodSymbol, ConcurrentBag<AnalyzedOrchestration>> OrchestrationsByMethod { get; }
    }

    /// <summary>
    /// Data structure to store the orchestration data.
    /// </summary>
    /// <param name="name">Name of the orchestration.</param>
    protected readonly struct AnalyzedOrchestration(string name)
    {
        /// <summary>
        /// Gets the name of the orchestration.
        /// </summary>
        public string Name { get; } = name;
    }
}
