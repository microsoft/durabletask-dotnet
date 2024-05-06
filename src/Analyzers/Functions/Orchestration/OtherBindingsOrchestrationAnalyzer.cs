// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.DurableTask.Analyzers.Orchestration;
using static Microsoft.DurableTask.Analyzers.Functions.Orchestration.OtherBindingsOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Functions.Orchestration;

/// <summary>
/// Analyzer that reports a warning when a Durable Function Orchestration has parameters bindings other than OrchestrationTrigger.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
class OtherBindingsOrchestrationAnalyzer : OrchestrationAnalyzer<OtherBindingsOrchestrationOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0008";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.OtherBindingsOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.OtherBindingsOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

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
    /// Visitor that inspects Durable Functions's method signatures for parameters binding other than OrchestrationTrigger.
    /// </summary>
    public sealed class OtherBindingsOrchestrationOrchestrationVisitor : OrchestrationVisitor
    {
        ImmutableArray<INamedTypeSymbol> bannedBindings;

        /// <inheritdoc/>
        public override bool Initialize()
        {
            List<INamedTypeSymbol?> candidateSymbols = [
                this.KnownTypeSymbols.DurableClientAttribute,
                this.KnownTypeSymbols.EntityTriggerAttribute,
                ];

            // filter out null values, since some of them may not be available during compilation
            this.bannedBindings = candidateSymbols.Where(s => s != null).ToImmutableArray()!;

            return this.bannedBindings.Length > 0;
        }

        /// <inheritdoc/>
        public override void VisitDurableFunction(SemanticModel sm, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            foreach (IParameterSymbol parameter in methodSymbol.Parameters)
            {
                IEnumerable<INamedTypeSymbol?> attributesSymbols = parameter.GetAttributes().Select(att => att.AttributeClass);

                if (attributesSymbols.Any(att => att != null && this.bannedBindings.Contains(att, SymbolEqualityComparer.Default)))
                {
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, parameter, orchestrationName));
                }
            }
        }
    }
}
