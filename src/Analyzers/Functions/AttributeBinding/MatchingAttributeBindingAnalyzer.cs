// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dapr.DurableTask.Analyzers.Functions.AttributeBinding;

/// <summary>
/// Expected attribute binding for a given parameter type.
/// </summary>
public struct ExpectedBinding
{
    /// <summary>
    /// Gets or sets the expected attribute.
    /// </summary>
    public INamedTypeSymbol? Attribute { get; set; }

    /// <summary>
    /// Gets or sets the expected type.
    /// </summary>
    public INamedTypeSymbol? Type { get; set; }
}

/// <summary>
/// Analyzer that inspects the parameter type of a method to ensure it matches the expected attribute binding.
/// It expects one parameter in the DiagnosticRule message template, so the analyzer can report the wrong type.
/// </summary>
public abstract class MatchingAttributeBindingAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(
            ctx =>
            {
                KnownTypeSymbols knownTypeSymbols = new(ctx.Compilation);

                ExpectedBinding expectedBinding = this.GetExpectedBinding(knownTypeSymbols);
                if (expectedBinding.Attribute is null || expectedBinding.Type is null)
                {
                    return;
                }

                ctx.RegisterSymbolAction(c => this.Analyze(c, expectedBinding), SymbolKind.Parameter);
            });
    }

    /// <summary>
    /// Gets the expected attribute binding and the related type to be used during parameters analysis.
    /// </summary>
    /// <param name="knownTypeSymbols">The set of well-known types.</param>
    /// <returns>The expected binding for this analyzer.</returns>
    protected abstract ExpectedBinding GetExpectedBinding(KnownTypeSymbols knownTypeSymbols);

    /// <summary>
    /// After an incorrect attribute/type matching is found, this method is called so the concrete implementation can report a diagnostic.
    /// </summary>
    /// <param name="ctx">Context for a symbol action. Allows reporting a diagnostic.</param>
    /// <param name="expected">Expected binding for an attribute/type.</param>
    /// <param name="parameter">Analyzed parameter symbol.</param>
    protected abstract void ReportDiagnostic(SymbolAnalysisContext ctx, ExpectedBinding expected, IParameterSymbol parameter);

    void Analyze(SymbolAnalysisContext ctx, ExpectedBinding expected)
    {
        IParameterSymbol parameter = (IParameterSymbol)ctx.Symbol;

        if (parameter.GetAttributes().Any(a => expected.Attribute!.Equals(a.AttributeClass, SymbolEqualityComparer.Default)))
        {
            if (!parameter.Type.Equals(expected.Type, SymbolEqualityComparer.Default))
            {
                this.ReportDiagnostic(ctx, expected, parameter);
            }
        }
    }
}
