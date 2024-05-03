// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;

/// <summary>
/// Analyzer that matches 'OrchestrationTriggerAttribute' with 'TaskOrchestrationContext' parameters.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrchestrationTriggerBindingAnalyzer : MatchingAttributeBindingAnalyzer
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE1001";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.OrchestrationTriggerBindingAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.OrchestrationTriggerBindingAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            Title,
            MessageFormat,
            AnalyzersCategories.AttributeBinding,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    protected override ExpectedBinding GetExpectedBinding(KnownTypeSymbols knownTypeSymbols)
    {
        return new ExpectedBinding()
        {
            Attribute = knownTypeSymbols.FunctionOrchestrationAttribute,
            Type = knownTypeSymbols.TaskOrchestrationContext,
        };
    }

    /// <inheritdoc/>
    protected override void ReportDiagnostic(SymbolAnalysisContext ctx, ExpectedBinding expected, IParameterSymbol parameter)
    {
        string wrongType = parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        ctx.ReportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, parameter, wrongType));
    }
}
