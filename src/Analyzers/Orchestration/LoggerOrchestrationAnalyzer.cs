// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.LoggerOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when a non-contextual ILogger is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoggerOrchestrationAnalyzer : OrchestrationAnalyzer<LoggerOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0009";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.LoggerOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.LoggerOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

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
    /// Visitor that inspects the method body for ILogger usage.
    /// </summary>
    public sealed class LoggerOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        INamedTypeSymbol? iLoggerSymbol;

        /// <inheritdoc/>
        public override bool Initialize()
        {
            this.iLoggerSymbol = this.KnownTypeSymbols.ILogger;
            if (this.iLoggerSymbol == null)
            {
                return false;
            }

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

            // Track which parameters we've already reported on to avoid duplicates
            HashSet<IParameterSymbol> reportedParameters = new(SymbolEqualityComparer.Default);

            // Check for ILogger parameters in the method signature
            foreach (IParameterSymbol parameter in methodSymbol.Parameters)
            {
                if (this.IsILoggerType(parameter.Type))
                {
                    // Found an ILogger parameter - report diagnostic at the parameter location
                    if (parameter.DeclaringSyntaxReferences.Length > 0)
                    {
                        SyntaxNode parameterSyntax = parameter.DeclaringSyntaxReferences[0].GetSyntax();
                        reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, parameterSyntax, methodSymbol.Name, orchestrationName));
                        reportedParameters.Add(parameter);
                    }
                }
            }

            // Check for ILogger field or property references (but not parameter references, as those were already reported)
            foreach (IOperation descendant in methodOperation.Descendants())
            {
                ITypeSymbol? typeToCheck = null;
                SyntaxNode? syntaxNode = null;

                switch (descendant)
                {
                    case IFieldReferenceOperation fieldRef:
                        typeToCheck = fieldRef.Field.Type;
                        syntaxNode = fieldRef.Syntax;
                        break;
                    case IPropertyReferenceOperation propRef:
                        typeToCheck = propRef.Property.Type;
                        syntaxNode = propRef.Syntax;
                        break;
                    case IParameterReferenceOperation paramRef:
                        // Skip parameter references that we already reported on in the parameter list
                        if (reportedParameters.Contains(paramRef.Parameter))
                        {
                            continue;
                        }

                        typeToCheck = paramRef.Parameter.Type;
                        syntaxNode = paramRef.Syntax;
                        break;
                }

                if (typeToCheck != null && syntaxNode != null && this.IsILoggerType(typeToCheck))
                {
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, syntaxNode, methodSymbol.Name, orchestrationName));
                }
            }
        }

        bool IsILoggerType(ITypeSymbol type)
        {
            if (this.iLoggerSymbol == null)
            {
                return false;
            }

            // First check for exact match with ILogger
            if (SymbolEqualityComparer.Default.Equals(type, this.iLoggerSymbol))
            {
                return true;
            }

            // Check if the type implements ILogger interface (covers ILogger<T> case)
            if (type is INamedTypeSymbol namedType)
            {
                foreach (INamedTypeSymbol interfaceType in namedType.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(interfaceType, this.iLoggerSymbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
