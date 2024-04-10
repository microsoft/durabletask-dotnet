// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeOrchestrationAnalyzer : OrchestrationAnalyzer
{
    public const string DiagnosticId = "DURABLE0001";

    static readonly LocalizableString title = new LocalizableResourceString(nameof(Resources.DateTimeOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString messageFormat = new LocalizableResourceString(nameof(Resources.DateTimeOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title,
        messageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    protected override void RegisterAdditionalCompilationStartAction(CompilationStartAnalysisContext context, OrchestrationAnalysisResult orchestrationAnalysisResult)
    {
        INamedTypeSymbol systemDateTimeSymbol = context.Compilation.GetSpecialType(SpecialType.System_DateTime);

        // stores the symbols (such as methods) and the DateTime references used in them
        ConcurrentBag<(ISymbol symbol, IPropertyReferenceOperation operation)> dateTimeUsage = [];

        // search for usages of DateTime.Now, DateTime.UtcNow, DateTime.Today and store them
        context.RegisterOperationAction(ctx =>
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();

            var operation = (IPropertyReferenceOperation)ctx.Operation;
            IPropertySymbol property = operation.Property;

            if (property.ContainingSymbol.Equals(systemDateTimeSymbol, SymbolEqualityComparer.Default) &&
                property.Name is nameof(DateTime.Now) or nameof(DateTime.UtcNow) or nameof(DateTime.Today))
            {
                ISymbol method = ctx.ContainingSymbol;
                dateTimeUsage.Add((method, operation));
            }
        }, OperationKind.PropertyReference);

        // compare whether the found DateTime usages occur in methods invoked by orchestrations
        context.RegisterCompilationEndAction(ctx =>
        {
            foreach ((ISymbol symbol, IPropertyReferenceOperation operation) in dateTimeUsage)
            {
                if (symbol is IMethodSymbol method)
                {
                    if (orchestrationAnalysisResult.OrchestrationsByMethod.TryGetValue(method, out ConcurrentBag<AnalyzedOrchestration> orchestrations))
                    {
                        string methodName = symbol.Name;
                        string dateTimePropertyName = operation.Property.ToString();
                        string orchestrationNames = string.Join(", ", orchestrations.Select(o => o.Name).OrderBy(n => n));

                        // e.g.: "The method 'Method1' uses 'System.Date.Now' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                        ctx.ReportDiagnostic(Rule, operation, methodName, dateTimePropertyName, orchestrationNames);
                    }
                }
            }
        });
    }
}
