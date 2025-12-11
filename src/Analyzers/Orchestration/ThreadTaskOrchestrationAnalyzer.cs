// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.DurableTask.Analyzers.Orchestration.ThreadTaskOrchestrationAnalyzer;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that detects usage of non-deterministic <see cref="Thread"/>/<see cref="Task"/> operations in orchestrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThreadTaskOrchestrationAnalyzer : OrchestrationAnalyzer<ThreadTaskOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0004";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.ThreadTaskOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.ThreadTaskOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

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
    /// Visitor that inspects the orchestration methods for non-deterministic <see cref="Thread"/>/<see cref="Task"/> operations.
    /// </summary>
    public sealed class ThreadTaskOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        /// <inheritdoc/>
        public override bool Initialize()
        {
            return this.KnownTypeSymbols.Thread != null &&
                this.KnownTypeSymbols.Task != null &&
                this.KnownTypeSymbols.TaskT != null &&
                this.KnownTypeSymbols.TaskFactory != null;
        }

        /// <inheritdoc/>
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            // reports usage of Thread.Start, Task.Run, Task.ContinueWith and Task.Factory.StartNew
            foreach (IInvocationOperation invocation in methodOperation.Descendants().OfType<IInvocationOperation>())
            {
                IMethodSymbol targetMethod = invocation.TargetMethod;

                if (targetMethod.IsEqualTo(this.KnownTypeSymbols.Thread, nameof(Thread.Start)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.Task, nameof(Task.Run)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskT, nameof(Task.Run)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.Task, nameof(Task.ContinueWith)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskT, nameof(Task.ContinueWith)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskFactory, nameof(TaskFactory.StartNew)) ||
                    targetMethod.IsEqualTo(this.KnownTypeSymbols.TaskFactoryT, nameof(TaskFactory.StartNew)))
                {
                    string invocationName = targetMethod.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

                    // e.g.: "The method 'Method1' uses non-deterministic Threads/Tasks operations by the invocation of 'Thread.Start' in orchestration 'MyOrchestrator'"
                    reportDiagnostic(RoslynExtensions.BuildDiagnostic(Rule, invocation, methodSymbol.Name, invocationName, orchestrationName));
                }
            }
        }
    }
}
