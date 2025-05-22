// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace Dapr.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Context information for the <see cref="OrchestrationContextFixer"/>.
/// </summary>
/// <param name="semanticModel">The Semantic Model retrieved from the Document.</param>
/// <param name="knownTypeSymbols">Well-known types that are used by the Durable analyzers.</param>
/// <param name="root">The root Syntax Node retrieved from the Document.</param>
/// <param name="syntaxNodeWithDiagnostic">Syntax Node that contains the diagnostic.</param>
/// <param name="taskOrchestrationContextSymbol">The 'TaskOrchestrationContext' symbol.</param>
public readonly struct OrchestrationCodeFixContext(
    SemanticModel semanticModel,
    KnownTypeSymbols knownTypeSymbols,
    SyntaxNode root,
    SyntaxNode syntaxNodeWithDiagnostic,
    IParameterSymbol taskOrchestrationContextSymbol)
{
    /// <summary>
    /// Gets the Semantic Model retrieved from the Document.
    /// </summary>
    public SemanticModel SemanticModel { get; } = semanticModel;

    /// <summary>
    /// Gets the well-known types that are used by the Durable analyzers.
    /// </summary>
    public KnownTypeSymbols KnownTypeSymbols { get; } = knownTypeSymbols;

    /// <summary>
    /// Gets the root Syntax Node retrieved from the Document.
    /// </summary>
    public SyntaxNode Root { get; } = root;

    /// <summary>
    /// Gets the Syntax Node that contains the diagnostic.
    /// </summary>
    public SyntaxNode SyntaxNodeWithDiagnostic { get; } = syntaxNodeWithDiagnostic;

    /// <summary>
    /// Gets the 'TaskOrchestrationContext' symbol.
    /// </summary>
    public IParameterSymbol TaskOrchestrationContextSymbol { get; } = taskOrchestrationContextSymbol;
}

/// <summary>
/// Base class for code fix providers that fix issues in orchestrator methods by replacing a SyntaxNode with a TaskOrchestrationContext member or invocation.
/// </summary>
public abstract class OrchestrationContextFixer : CodeFixProvider
{
    /// <inheritdoc/>
    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null)
        {
            return;
        }

        // Find the Syntax Node that is causing the diagnostic.
        if (root.FindNode(context.Span, getInnermostNodeForTie: true) is not SyntaxNode syntaxNodeWithDiagnostic)
        {
            return;
        }

        SemanticModel? semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
        if (semanticModel == null)
        {
            return;
        }

        // Analyze the data flow to determine which variables are assigned (reachable) within the scope.
        DataFlowAnalysis? dataFlowAnalysis = semanticModel.AnalyzeDataFlow(syntaxNodeWithDiagnostic);
        if (dataFlowAnalysis == null)
        {
            return;
        }

        KnownTypeSymbols knownTypeSymbols = new(semanticModel.Compilation);
        if (knownTypeSymbols.TaskOrchestrationContext == null)
        {
            return;
        }

        // Find the TaskOrchestrationContext parameter available in the scope.
        IParameterSymbol? taskOrchestrationContextSymbol = dataFlowAnalysis.DefinitelyAssignedOnEntry
            .OfType<IParameterSymbol>()
            .FirstOrDefault(
                p => p.Type.Equals(knownTypeSymbols.TaskOrchestrationContext, SymbolEqualityComparer.Default));
        if (taskOrchestrationContextSymbol == null)
        {
            // This method does not have a TaskOrchestrationContext parameter, so we should not offer this code fix.
            return;
        }

        var orchestrationContext = new OrchestrationCodeFixContext(
            semanticModel, knownTypeSymbols, root, syntaxNodeWithDiagnostic, taskOrchestrationContextSymbol);

        this.RegisterCodeFixes(context, orchestrationContext);
    }

    /// <summary>
    /// Registers a code fix for an orchestration diagnostic that can be fixed by replacing a SyntaxNode with a TaskOrchestrationContext member or invocation.
    /// </summary>
    /// <param name="context">A <see cref="CodeFixContext"/> containing context information about the diagnostics to fix.</param>
    /// <param name="orchestrationContext">A <see cref="OrchestrationCodeFixContext"/> containing context information about the orchestration code fixer.</param>
    protected abstract void RegisterCodeFixes(CodeFixContext context, OrchestrationCodeFixContext orchestrationContext);
}
