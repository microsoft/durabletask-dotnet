// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.DateTimeOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when a non-deterministic DateTime, DateTimeOffset, or TimeProvider method is used in an orchestration method.
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
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects the method body for DateTime and DateTimeOffset properties, and TimeProvider method invocations.
    /// </summary>
    public sealed class DateTimeOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        INamedTypeSymbol systemDateTimeSymbol = null!;
        INamedTypeSymbol? systemDateTimeOffsetSymbol;
        INamedTypeSymbol? systemTimeProviderSymbol;

        /// <inheritdoc/>
        public override bool Initialize()
        {
            this.systemDateTimeSymbol = this.Compilation.GetSpecialType(SpecialType.System_DateTime);
            this.systemDateTimeOffsetSymbol = this.Compilation.GetTypeByMetadataName("System.DateTimeOffset");
            this.systemTimeProviderSymbol = this.Compilation.GetTypeByMetadataName("System.TimeProvider");
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

            // Check for TimeProvider method invocations
            if (this.systemTimeProviderSymbol is not null)
            {
                foreach (IInvocationOperation operation in methodOperation.Descendants().OfType<IInvocationOperation>())
                {
                    IMethodSymbol invokedMethod = operation.TargetMethod;

                    // Check if the method is called on TimeProvider type
                    bool isTimeProvider = invokedMethod.ContainingType.Equals(this.systemTimeProviderSymbol, SymbolEqualityComparer.Default);

                    if (!isTimeProvider)
                    {
                        continue;
                    }

                    // Check for non-deterministic TimeProvider methods: GetUtcNow, GetLocalNow, GetTimestamp
                    bool isNonDeterministicMethod = invokedMethod.Name is "GetUtcNow" or "GetLocalNow" or "GetTimestamp";

                    if (isNonDeterministicMethod)
                    {
                        // e.g.: "The method 'Method1' uses 'System.TimeProvider.GetUtcNow()' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                        string timeProviderMethodName = $"{invokedMethod.ContainingType}.{invokedMethod.Name}()";
                        reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, operation.Syntax, methodSymbol.Name, timeProviderMethodName, orchestrationName));
                    }
                }
            }
        }
    }
}
