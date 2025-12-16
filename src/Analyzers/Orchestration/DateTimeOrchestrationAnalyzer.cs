// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.DateTimeOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when a non-deterministic DateTime or DateTimeOffset property is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeOrchestrationAnalyzer : OrchestrationAnalyzer<DateTimeOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0001";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.DateTimeOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.DateTimeOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: $"https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects the method body for DateTime and DateTimeOffset properties.
    /// </summary>
    public sealed class DateTimeOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        INamedTypeSymbol systemDateTimeSymbol = null!;
        INamedTypeSymbol? systemDateTimeOffsetSymbol;

        /// <inheritdoc/>
        public override bool Initialize()
        {
            this.systemDateTimeSymbol = this.Compilation.GetSpecialType(SpecialType.System_DateTime);
            this.systemDateTimeOffsetSymbol = this.Compilation.GetTypeByMetadataName("System.DateTimeOffset");
            return true;
        }

        /// <inheritdoc/>
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            foreach (IPropertyReferenceOperation operation in methodOperation.Descendants().OfType<IPropertyReferenceOperation>())
            {
                IPropertySymbol property = operation.Property;

                bool isDateTime = property.ContainingSymbol.Equals(this.systemDateTimeSymbol, SymbolEqualityComparer.Default);
                bool isDateTimeOffset = this.systemDateTimeOffsetSymbol is not null &&
                                       property.ContainingSymbol.Equals(this.systemDateTimeOffsetSymbol, SymbolEqualityComparer.Default);

                if (!isDateTime && !isDateTimeOffset)
                {
                    continue;
                }

                // Check for non-deterministic properties
                // DateTime has: Now, UtcNow, Today
                // DateTimeOffset has: Now, UtcNow (but not Today)
                bool isNonDeterministic = property.Name is nameof(DateTime.Now) or nameof(DateTime.UtcNow) ||
                                         (isDateTime && property.Name == nameof(DateTime.Today));

                if (isNonDeterministic)
                {
                    // e.g.: "The method 'Method1' uses 'System.DateTime.Now' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                    // e.g.: "The method 'Method1' uses 'System.DateTimeOffset.Now' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, operation.Syntax, methodSymbol.Name, property.ToString(), orchestrationName));
                }
            }
        }
    }
}
