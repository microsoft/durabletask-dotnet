// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

using static Microsoft.DurableTask.Analyzers.Orchestration.IOOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports usage of I/O APIs in orchestrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IOOrchestrationAnalyzer : OrchestrationAnalyzer<IOOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0005";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.IOOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.IOOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

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
    /// Visitor that inspects the method body for I/O operations by searching for specific types.
    /// </summary>
    public sealed class IOOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        ImmutableArray<INamedTypeSymbol> bannedTypes;

        /// <inheritdoc/>
        public override bool Initialize()
        {
            List<INamedTypeSymbol?> candidateSymbols = [
                this.KnownTypeSymbols.HttpClient,
                this.KnownTypeSymbols.BlobServiceClient,
                this.KnownTypeSymbols.BlobContainerClient,
                this.KnownTypeSymbols.BlobClient,
                this.KnownTypeSymbols.QueueServiceClient,
                this.KnownTypeSymbols.QueueClient,
                this.KnownTypeSymbols.TableServiceClient,
                this.KnownTypeSymbols.TableClient,
                this.KnownTypeSymbols.CosmosClient,
                this.KnownTypeSymbols.SqlConnection,
                ];

            // filter out null values, since some of them may not be available during compilation:
            this.bannedTypes = candidateSymbols.Where(s => s is not null).ToImmutableArray()!;

            return this.bannedTypes.Length > 0;
        }

        /// <inheritdoc/>
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            foreach (IOperation operation in methodOperation.Descendants())
            {
                if (operation.Type is not null)
                {
                    if (this.bannedTypes.Contains(operation.Type, SymbolEqualityComparer.Default))
                    {
                        string typeName = operation.Type.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

                        // e.g.: "The method 'Method1' performs I/O through 'HttpClient' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
                        reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, operation, methodSymbol.Name, typeName, orchestrationName));
                    }
                }
            }
        }
    }
}
