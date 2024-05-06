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
/// <typeparam name="TOrchestrationVisitor">Orchestration Visitor to be used during Orchestrations discovery.</typeparam>
public abstract class OrchestrationAnalyzer<TOrchestrationVisitor> : DiagnosticAnalyzer where TOrchestrationVisitor : OrchestrationVisitor, new()
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
                knownSymbols.TaskOrchestratorInterface == null ||
                knownSymbols.DurableTaskRegistry == null)
            {
                // symbols not available in this compilation, skip analysis
                return;
            }

            TOrchestrationVisitor visitor = new();
            if (!visitor.Initialize(context.Compilation, knownSymbols))
            {
                return;
            }

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

                var rootMethodSyntax = (MethodDeclarationSyntax)ctx.Node;

                visitor.VisitDurableFunction(ctx.SemanticModel, rootMethodSyntax, methodSymbol, functionName, ctx.ReportDiagnostic);
            },
                SyntaxKind.MethodDeclaration);

            // look for ITaskOrchestrator/TaskOrchestrator`2 Orchestrations
            context.RegisterSyntaxNodeAction(
                ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                if (ctx.ContainingSymbol is not INamedTypeSymbol classSymbol)
                {
                    return;
                }

                bool implementsITaskOrchestrator = classSymbol.AllInterfaces.Any(i => i.Equals(knownSymbols.TaskOrchestratorInterface, SymbolEqualityComparer.Default));
                if (!implementsITaskOrchestrator)
                {
                    return;
                }

                IEnumerable<IMethodSymbol> orchestrationMethods = classSymbol.GetMembers().OfType<IMethodSymbol>()
                    .Where(m => m.Parameters.Any(p => p.Type.Equals(knownSymbols.TaskOrchestrationContext, SymbolEqualityComparer.Default)));

                string functionName = classSymbol.Name;

                foreach (IMethodSymbol? methodSymbol in orchestrationMethods)
                {
                    IEnumerable<MethodDeclarationSyntax> methodSyntaxes = methodSymbol.GetSyntaxNodes();
                    foreach (MethodDeclarationSyntax rootMethodSyntax in methodSyntaxes)
                    {
                        visitor.VisitTaskOrchestrator(ctx.SemanticModel, rootMethodSyntax, methodSymbol, functionName, ctx.ReportDiagnostic);
                    }
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
                SyntaxNode? methodSyntax = null;
                switch (delegateCreationOperation.Target)
                {
                    case IAnonymousFunctionOperation lambdaOperation:
                        // use the containing symbol of the lambda (e.g. the class declaring it) as the method symbol
                        methodSymbol = ctx.ContainingSymbol as IMethodSymbol;
                        methodSyntax = delegateCreationOperation.Syntax;
                        break;
                    case IMethodReferenceOperation methodReferenceOperation:
                        // use the method reference as the method symbol
                        methodSymbol = methodReferenceOperation.Method;
                        methodSyntax = methodReferenceOperation.Method.DeclaringSyntaxReferences.First().GetSyntax();
                        break;
                    default:
                        break;
                }

                if (methodSymbol == null || methodSyntax == null)
                {
                    return;
                }

                // try to get the name of the orchestration from the method call, otherwise use the containing type name
                IArgumentOperation nameArgument = invocation.Arguments.First(a => a.Parameter!.Name == "name");
                Optional<object?> name = nameArgument.GetConstantValueFromAttribute(ctx.Operation.SemanticModel!, ctx.CancellationToken);
                string orchestrationName = name.Value?.ToString() ?? methodSymbol.Name;

                visitor.VisitFuncOrchestrator(ctx.Operation.SemanticModel!, methodSyntax, methodSymbol, orchestrationName, ctx.ReportDiagnostic);
            },
                OperationKind.Invocation);
        });
    }
}

/// <summary>
/// An Orchestration Visitor allows a concrete implementation of an analyzer to visit different types of orchestrations,
/// such as Durable Functions, TaskOrchestrator, ITaskOrchestrator, and OrchestratorFunc.
/// It provides a set of methods that can be overridden to inspect different types of orchestrations.
/// Besides, it provides a method to initialize the visitor members, checking for available symbols and
/// return whether the concrete implementation visitor should continue running and perform its analysis.
/// </summary>
public abstract class OrchestrationVisitor
{
    /// <summary>
    /// Gets the Compilation instance acquired from the analyzer context, including syntax trees, semantic models, and other information.
    /// </summary>
    protected Compilation Compilation { get; private set; } = null!;

    /// <summary>
    /// Gets the set of well-known type symbols.
    /// </summary>
    protected KnownTypeSymbols KnownTypeSymbols { get; private set; } = null!;

    /// <summary>
    /// Initializes the visitor members and returns whether the concrete implementation visitor was initialized.
    /// </summary>
    /// <param name="compilation">The compilation acquired from analyzer context.</param>
    /// <param name="knownTypeSymbols">The set of well-known type symbols instance.</param>
    /// <returns>True if the analyzer can continue; false otherwise.</returns>
    public bool Initialize(Compilation compilation, KnownTypeSymbols knownTypeSymbols)
    {
        this.Compilation = compilation;
        this.KnownTypeSymbols = knownTypeSymbols;

        return this.Initialize();
    }

    /// <summary>
    /// Initializes a visitor concrete implementation instance and returns whether the analysis should continue.
    /// </summary>
    /// <returns>True if the analyzer can continue; false otherwise.</returns>
    public virtual bool Initialize() => true;

    /// <summary>
    /// Visits a Durable Function orchestration.
    /// </summary>
    /// <param name="semanticModel">Semantic Model.</param>
    /// <param name="methodSyntax">Method Syntax Node.</param>
    /// <param name="methodSymbol">Method Symbol.</param>
    /// <param name="orchestrationName">Durable Function name.</param>
    /// <param name="reportDiagnostic">Function that can be used to report diagnostics.</param>
    public virtual void VisitDurableFunction(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
    }

    /// <summary>
    /// Visits a strongly typed Task Orchestrator that implements an ITaskOrchestrator orchestration.
    /// </summary>
    /// <param name="semanticModel">Semantic Model.</param>
    /// <param name="methodSyntax">Method Syntax Node.</param>
    /// <param name="methodSymbol">Method Symbol.</param>
    /// <param name="orchestrationName">Class name.</param>
    /// <param name="reportDiagnostic">Function that can be used to report diagnostics.</param>
    public virtual void VisitTaskOrchestrator(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
    }

    /// <summary>
    /// Visits an Orchestrator Func orchestration.
    /// </summary>
    /// <param name="semanticModel">Semantic Model.</param>
    /// <param name="methodSyntax">Method Syntax Node.</param>
    /// <param name="methodSymbol">Method Symbol.</param>
    /// <param name="orchestrationName">Class name.</param>
    /// <param name="reportDiagnostic">Function that can be used to report diagnostics.</param>
    public virtual void VisitFuncOrchestrator(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
    }
}

/// <summary>
/// Visitor that recursively inspects the methods invoked by an orchestration root method.
/// </summary>
public class MethodProbeOrchestrationVisitor : OrchestrationVisitor
{
    readonly ConcurrentDictionary<IMethodSymbol, ConcurrentBag<string>> orchestrationsByMethod = new(SymbolEqualityComparer.Default);

    /// <inheritdoc/>
    public override void VisitDurableFunction(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
        this.FindInvokedMethods(semanticModel, methodSyntax, methodSymbol, orchestrationName, reportDiagnostic);
    }

    /// <inheritdoc/>
    public override void VisitTaskOrchestrator(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
        this.FindInvokedMethods(semanticModel, methodSyntax, methodSymbol, orchestrationName, reportDiagnostic);
    }

    /// <inheritdoc/>
    public override void VisitFuncOrchestrator(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
        this.FindInvokedMethods(semanticModel, methodSyntax, methodSymbol, orchestrationName, reportDiagnostic);
    }

    /// <summary>
    /// Visits a method independent of the orchestration type.
    /// </summary>
    /// <param name="semanticModel">Semantic Model.</param>
    /// <param name="methodSyntax">Method Syntax Node.</param>
    /// <param name="methodSymbol">Method Symbol.</param>
    /// <param name="orchestrationName">Orchestration name.</param>
    /// <param name="reportDiagnostic">Function that can be used to report diagnostics.</param>
    protected virtual void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
    {
    }

    // Recursively find all methods invoked by the orchestration root method and call the appropriate visitor method
    void FindInvokedMethods(
        SemanticModel semanticModel,
        SyntaxNode callerSyntax,
        IMethodSymbol callerSymbol,
        string rootOrchestration,
        Action<Diagnostic> reportDiagnostic)
    {
        // add the visited method to the list of orchestrations
        ConcurrentBag<string> orchestrations = this.orchestrationsByMethod.GetOrAdd(callerSymbol, []);
        if (orchestrations.Contains(rootOrchestration))
        {
            // previously tracked method, leave to avoid infinite recursion
            return;
        }

        orchestrations.Add(rootOrchestration);

        // allow derived visitors to inspect methods independent of the specific orchestration type:
        this.VisitMethod(semanticModel, callerSyntax, callerSymbol, rootOrchestration, reportDiagnostic);

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
                this.FindInvokedMethods(semanticModel, calleeSyntax, calleeMethodSymbol, rootOrchestration, reportDiagnostic);
            }
        }
    }
}
