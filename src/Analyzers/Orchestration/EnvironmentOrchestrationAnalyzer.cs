// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.EnvironmentOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

#pragma warning disable RS1035 // Environment Variables are not supposed to be used in Analyzers, but here we just reference the API, never using it.

/// <summary>
/// Analyzer that reports usage of <see cref="System.Environment"/> APIs in orchestrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnvironmentOrchestrationAnalyzer : OrchestrationAnalyzer<EnvironmentOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0006";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.EnvironmentOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.EnvironmentOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://go.microsoft.com/fwlink/?linkid=2346202");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that inspects the method body for retrievals of Environment Variables through the <see cref="System.Environment" /> type.
    /// </summary>
    public sealed class EnvironmentOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.Environment != null
                || this.KnownTypeSymbols.IConfiguration != null
                || this.KnownTypeSymbols.IOptions != null
                || this.KnownTypeSymbols.IOptionsSnapshot != null
                || this.KnownTypeSymbols.IOptionsMonitor != null;
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
                this.CheckOperationForEnvironmentVariableAccess(operation, methodSymbol, orchestrationName, reportDiagnostic);
            }
        }

        void CheckOperationForEnvironmentVariableAccess(IOperation operation, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            switch (operation)
            {
                case IInvocationOperation invocation when this.IsEnvironmentInvocation(invocation.TargetMethod):
                    this.Report(operation, invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), methodSymbol, orchestrationName, reportDiagnostic);
                    break;
                case IInvocationOperation invocation when this.IsConfigurationOrOptionsInvocation(invocation):
                    this.Report(operation, invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), methodSymbol, orchestrationName, reportDiagnostic);
                    break;
                case IPropertyReferenceOperation propertyReference when this.IsConfigurationOrOptionsPropertyReference(propertyReference):
                    this.Report(operation, propertyReference.Property.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), methodSymbol, orchestrationName, reportDiagnostic);
                    break;
            }
        }

        void Report(IOperation operation, string memberName, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, operation, methodSymbol.Name, memberName, orchestrationName));
        }

        bool IsEnvironmentInvocation(IMethodSymbol targetMethod)
        {
            return this.KnownTypeSymbols.Environment != null &&
                   targetMethod.ContainingType.Equals(this.KnownTypeSymbols.Environment, SymbolEqualityComparer.Default) &&
                   targetMethod.Name is nameof(Environment.GetEnvironmentVariable) or nameof(Environment.GetEnvironmentVariables) or nameof(Environment.ExpandEnvironmentVariables);
        }

        bool IsConfigurationOrOptionsInvocation(IInvocationOperation invocation)
        {
            if (this.IsConfigurationOrOptionsType(invocation.Instance?.Type))
            {
                return true;
            }

            if (invocation.TargetMethod.IsExtensionMethod)
            {
                ITypeSymbol? receiverType = invocation.TargetMethod.ReducedFrom?.Parameters.FirstOrDefault()?.Type ?? invocation.TargetMethod.Parameters.FirstOrDefault()?.Type;
                if (this.IsConfigurationOrOptionsType(receiverType))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsConfigurationOrOptionsPropertyReference(IPropertyReferenceOperation propertyReference)
        {
            if (this.IsConfigurationOrOptionsType(propertyReference.Instance?.Type))
            {
                return true;
            }

            return this.IsConfigurationOrOptionsType(propertyReference.Property.ContainingType);
        }

        bool IsConfigurationOrOptionsType(ITypeSymbol? type)
        {
            if (type is null)
            {
                return false;
            }

            if (this.IsConfigurationType(type))
            {
                return true;
            }

            if (type is INamedTypeSymbol namedType && this.IsOptionsType(namedType))
            {
                return true;
            }

            return type.AllInterfaces.Any(this.IsConfigurationType) ||
                   (type is INamedTypeSymbol typeSymbol && typeSymbol.AllInterfaces.Any(this.IsOptionsType));
        }

        bool IsConfigurationType(ITypeSymbol type)
        {
            return this.KnownTypeSymbols.IConfiguration != null &&
                   SymbolEqualityComparer.Default.Equals(type, this.KnownTypeSymbols.IConfiguration);
        }

        bool IsOptionsType(INamedTypeSymbol type)
        {
            return this.IsOptionsType(type, this.KnownTypeSymbols.IOptions)
                   || this.IsOptionsType(type, this.KnownTypeSymbols.IOptionsSnapshot)
                   || this.IsOptionsType(type, this.KnownTypeSymbols.IOptionsMonitor);
        }

        bool IsOptionsType(INamedTypeSymbol type, INamedTypeSymbol? optionsSymbol)
        {
            return optionsSymbol != null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, optionsSymbol);
        }
    }
}
