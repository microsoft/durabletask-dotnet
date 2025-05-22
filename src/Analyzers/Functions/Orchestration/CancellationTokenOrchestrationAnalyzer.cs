// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Dapr.DurableTask.Analyzers.Orchestration;
using static Dapr.DurableTask.Analyzers.Functions.Orchestration.CancellationTokenOrchestrationAnalyzer;

namespace Dapr.DurableTask.Analyzers.Functions.Orchestration;

/// <summary>
/// Analyzer that reports a warning when CancellationToken is used in a Durable Functions Orchestration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CancellationTokenOrchestrationAnalyzer : OrchestrationAnalyzer<CancellationTokenOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0007";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.CancellationTokenOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.CancellationTokenOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects Durable Functions's method signatures for CancellationToken parameters.
    /// </summary>
    public sealed class CancellationTokenOrchestrationVisitor : OrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.CancellationToken is not null;
        }

        /// <inheritdoc/>
        public override void VisitDurableFunction(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            foreach (IParameterSymbol parameter in methodSymbol.Parameters)
            {
                if (parameter.Type.Equals(this.KnownTypeSymbols.CancellationToken, SymbolEqualityComparer.Default))
                {
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, parameter, orchestrationName));
                }
            }
        }
    }
}
