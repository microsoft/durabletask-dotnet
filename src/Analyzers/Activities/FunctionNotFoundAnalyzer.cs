// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers.Activities;

/// <summary>
/// Analyzer that detects calls to non-existent activities and sub-orchestrations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FunctionNotFoundAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for the diagnostic that reports when an activity call references a function that doesn't exist.
    /// </summary>
    public const string ActivityNotFoundDiagnosticId = "DURABLE2003";

    /// <summary>
    /// The diagnostic ID for the diagnostic that reports when a sub-orchestration call references a function that doesn't exist.
    /// </summary>
    public const string SubOrchestrationNotFoundDiagnosticId = "DURABLE2004";

    // System assemblies to skip when scanning referenced assemblies for performance
    static readonly HashSet<string> SystemAssemblyNames =
    [
        "mscorlib",
        "System",
        "netstandard"
    ];

    static readonly string[] SystemAssemblyPrefixes =
    [
        "System.",
        "Microsoft.CodeAnalysis",
        "Microsoft.CSharp",
        "Microsoft.VisualBasic"
    ];

    static readonly LocalizableString ActivityNotFoundTitle = new LocalizableResourceString(nameof(Resources.ActivityNotFoundAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString ActivityNotFoundMessageFormat = new LocalizableResourceString(nameof(Resources.ActivityNotFoundAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly LocalizableString SubOrchestrationNotFoundTitle = new LocalizableResourceString(nameof(Resources.SubOrchestrationNotFoundAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString SubOrchestrationNotFoundMessageFormat = new LocalizableResourceString(nameof(Resources.SubOrchestrationNotFoundAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor ActivityNotFoundRule = new(
        ActivityNotFoundDiagnosticId,
        ActivityNotFoundTitle,
        ActivityNotFoundMessageFormat,
        AnalyzersCategories.Activity,
        DiagnosticSeverity.Warning,
        customTags: [WellKnownDiagnosticTags.CompilationEnd],
        isEnabledByDefault: true,
        helpLinkUri: "https://go.microsoft.com/fwlink/?linkid=2346202");

    static readonly DiagnosticDescriptor SubOrchestrationNotFoundRule = new(
        SubOrchestrationNotFoundDiagnosticId,
        SubOrchestrationNotFoundTitle,
        SubOrchestrationNotFoundMessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        customTags: [WellKnownDiagnosticTags.CompilationEnd],
        isEnabledByDefault: true,
        helpLinkUri: "https://go.microsoft.com/fwlink/?linkid=2346202");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ActivityNotFoundRule, SubOrchestrationNotFoundRule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(context =>
        {
            KnownTypeSymbols knownSymbols = new(context.Compilation);

            if (knownSymbols.TaskOrchestrationContext == null ||
                knownSymbols.Task == null || knownSymbols.TaskT == null)
            {
                // Core symbols not available in this compilation, skip analysis
                return;
            }

            // Activity-related symbols (may be null if activities aren't used)
            IMethodSymbol? taskActivityRunAsync = knownSymbols.TaskActivityBase?.GetMembers("RunAsync").OfType<IMethodSymbol>().SingleOrDefault();

            // Search for Activity and Sub-Orchestrator invocations
            ConcurrentBag<FunctionInvocation> activityInvocations = [];
            ConcurrentBag<FunctionInvocation> subOrchestrationInvocations = [];

            context.RegisterOperationAction(
                ctx =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();

                    if (ctx.Operation is not IInvocationOperation invocationOperation)
                    {
                        return;
                    }

                    IMethodSymbol targetMethod = invocationOperation.TargetMethod;

                    // Check for CallActivityAsync
                    if (targetMethod.IsEqualTo(knownSymbols.TaskOrchestrationContext, "CallActivityAsync"))
                    {
                        string? activityName = ExtractFunctionName(invocationOperation, "name", ctx);
                        if (activityName != null)
                        {
                            activityInvocations.Add(new FunctionInvocation(activityName, invocationOperation.Syntax));
                        }
                    }

                    // Check for CallSubOrchestratorAsync
                    if (targetMethod.IsEqualTo(knownSymbols.TaskOrchestrationContext, "CallSubOrchestratorAsync"))
                    {
                        string? orchestratorName = ExtractFunctionName(invocationOperation, "orchestratorName", ctx);
                        if (orchestratorName != null)
                        {
                            subOrchestrationInvocations.Add(new FunctionInvocation(orchestratorName, invocationOperation.Syntax));
                        }
                    }
                },
                OperationKind.Invocation);

            // Search for Activity definitions
            ConcurrentBag<string> activityNames = [];
            ConcurrentBag<string> orchestratorNames = [];

            // Search for Durable Functions Activities and Orchestrators definitions (via [Function] attribute)
            context.RegisterSymbolAction(
                ctx =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();

                    if (ctx.Symbol is not IMethodSymbol methodSymbol)
                    {
                        return;
                    }

                    // Check for Activity defined via [ActivityTrigger]
                    if (IsActivityMethod(methodSymbol, knownSymbols, out string functionName))
                    {
                        activityNames.Add(functionName);
                    }

                    // Check for Orchestrator defined via [OrchestrationTrigger]
                    if (IsOrchestratorMethod(methodSymbol, knownSymbols, out string orchestratorFunctionName))
                    {
                        orchestratorNames.Add(orchestratorFunctionName);
                    }
                },
                SymbolKind.Method);

            // Search for TaskActivity<TInput, TOutput> definitions (class-based syntax)
            context.RegisterSyntaxNodeAction(
                ctx =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();

                    if (ctx.ContainingSymbol is not INamedTypeSymbol classSymbol)
                    {
                        return;
                    }

                    if (classSymbol.IsAbstract)
                    {
                        return;
                    }

                    // Check for TaskActivity<TInput, TOutput> derived classes
                    if (knownSymbols.TaskActivityBase != null && taskActivityRunAsync != null &&
                        ClassOverridesMethod(classSymbol, taskActivityRunAsync))
                    {
                        activityNames.Add(classSymbol.Name);
                    }

                    // Check for ITaskOrchestrator implementations (class-based orchestrators)
                    if (knownSymbols.TaskOrchestratorInterface != null &&
                        ImplementsInterface(classSymbol, knownSymbols.TaskOrchestratorInterface))
                    {
                        orchestratorNames.Add(classSymbol.Name);
                    }
                },
                SyntaxKind.ClassDeclaration);

            // Search for Func/Action activities directly registered through DurableTaskRegistry
            context.RegisterOperationAction(
                ctx =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();

                    if (ctx.Operation is not IInvocationOperation invocation)
                    {
                        return;
                    }

                    if (knownSymbols.DurableTaskRegistry == null ||
                        !SymbolEqualityComparer.Default.Equals(invocation.Type, knownSymbols.DurableTaskRegistry))
                    {
                        return;
                    }

                    // Handle AddActivityFunc registrations
                    if (invocation.TargetMethod.Name == "AddActivityFunc")
                    {
                        string? name = ExtractFunctionName(invocation, "name", ctx);
                        if (name != null)
                        {
                            activityNames.Add(name);
                        }
                    }

                    // Handle AddOrchestratorFunc registrations
                    if (invocation.TargetMethod.Name == "AddOrchestratorFunc")
                    {
                        string? name = ExtractFunctionName(invocation, "name", ctx);
                        if (name != null)
                        {
                            orchestratorNames.Add(name);
                        }
                    }
                },
                OperationKind.Invocation);

            // At the end of the compilation, we correlate the invocations with the definitions
            context.RegisterCompilationEndAction(ctx =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                // Scan referenced assemblies for activities and orchestrators
                ScanReferencedAssemblies(
                    ctx.Compilation,
                    knownSymbols,
                    taskActivityRunAsync,
                    activityNames,
                    orchestratorNames,
                    ctx.CancellationToken);

                // Create lookup sets for faster searching
                HashSet<string> definedActivities = new(activityNames);
                HashSet<string> definedOrchestrators = new(orchestratorNames);

                // Report diagnostics for activities not found
                foreach (FunctionInvocation invocation in activityInvocations.Where(i => !definedActivities.Contains(i.Name)))
                {
                    Diagnostic diagnostic = RoslynExtensions.BuildDiagnostic(
                        ActivityNotFoundRule, invocation.InvocationSyntaxNode, invocation.Name);
                    ctx.ReportDiagnostic(diagnostic);
                }

                // Report diagnostics for sub-orchestrators not found
                foreach (FunctionInvocation invocation in subOrchestrationInvocations.Where(i => !definedOrchestrators.Contains(i.Name)))
                {
                    Diagnostic diagnostic = RoslynExtensions.BuildDiagnostic(
                        SubOrchestrationNotFoundRule, invocation.InvocationSyntaxNode, invocation.Name);
                    ctx.ReportDiagnostic(diagnostic);
                }
            });
        });
    }

    static string? ExtractFunctionName(IInvocationOperation invocationOperation, string parameterName, OperationAnalysisContext ctx)
    {
        IArgumentOperation? nameArgumentOperation = invocationOperation.Arguments.SingleOrDefault(a => a.Parameter?.Name == parameterName);
        if (nameArgumentOperation == null)
        {
            return null;
        }

        SemanticModel? semanticModel = ctx.Operation.SemanticModel;
        if (semanticModel == null)
        {
            return null;
        }

        // extracts the constant value from the argument (e.g.: it can be a nameof, string literal or const field)
        Optional<object?> constant = semanticModel.GetConstantValue(nameArgumentOperation.Value.Syntax);
        if (!constant.HasValue)
        {
            // not a constant value, we cannot correlate this invocation to an existent function in compile time
            return null;
        }

        return constant.Value?.ToString();
    }

    static bool IsActivityMethod(IMethodSymbol methodSymbol, KnownTypeSymbols knownSymbols, out string functionName)
    {
        functionName = string.Empty;

        if (knownSymbols.ActivityTriggerAttribute == null ||
            !methodSymbol.ContainsAttributeInAnyMethodArguments(knownSymbols.ActivityTriggerAttribute))
        {
            return false;
        }

        if (knownSymbols.FunctionNameAttribute == null ||
            !methodSymbol.TryGetSingleValueFromAttribute(knownSymbols.FunctionNameAttribute, out functionName))
        {
            return false;
        }

        return true;
    }

    static bool IsOrchestratorMethod(IMethodSymbol methodSymbol, KnownTypeSymbols knownSymbols, out string functionName)
    {
        functionName = string.Empty;

        if (knownSymbols.FunctionOrchestrationAttribute == null ||
            !methodSymbol.ContainsAttributeInAnyMethodArguments(knownSymbols.FunctionOrchestrationAttribute))
        {
            return false;
        }

        if (knownSymbols.FunctionNameAttribute == null ||
            !methodSymbol.TryGetSingleValueFromAttribute(knownSymbols.FunctionNameAttribute, out functionName))
        {
            return false;
        }

        return true;
    }

    static bool ImplementsInterface(INamedTypeSymbol typeSymbol, INamedTypeSymbol interfaceSymbol)
    {
        return typeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceSymbol));
    }

    static bool ClassOverridesMethod(INamedTypeSymbol classSymbol, IMethodSymbol methodToFind)
    {
        INamedTypeSymbol? baseType = classSymbol;
        while (baseType != null)
        {
            if (baseType.GetMembers().OfType<IMethodSymbol>()
                .Any(method => SymbolEqualityComparer.Default.Equals(method.OverriddenMethod?.OriginalDefinition, methodToFind)))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    static void ScanReferencedAssemblies(
        Compilation compilation,
        KnownTypeSymbols knownSymbols,
        IMethodSymbol? taskActivityRunAsync,
        ConcurrentBag<string> activityNames,
        ConcurrentBag<string> orchestratorNames,
        CancellationToken cancellationToken)
    {
        // Scan all referenced assemblies for activities and orchestrators
        // Skip system assemblies and assemblies without Durable Task references for performance
        foreach (MetadataReference reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            {
                continue;
            }

            if (IsSystemAssembly(assembly))
            {
                continue;
            }

            if (!ShouldScanAssembly(assembly))
            {
                continue;
            }

            // Scan this assembly
            ScanNamespaceForFunctions(
                assembly.GlobalNamespace,
                knownSymbols,
                taskActivityRunAsync,
                activityNames,
                orchestratorNames,
                cancellationToken);
        }
    }

    static bool IsSystemAssembly(IAssemblySymbol assembly)
    {
        // Skip well-known system assemblies to improve performance
        string assemblyName = assembly.Name;

        if (SystemAssemblyNames.Contains(assemblyName))
        {
            return true;
        }

        if (SystemAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    static bool ShouldScanAssembly(IAssemblySymbol assembly)
    {
        // Only scan assemblies that reference Durable Task types
        // This filters out transitive dependencies that don't contain activities/orchestrators
        foreach (AssemblyIdentity referencedAssembly in assembly.Modules.SelectMany(m => m.ReferencedAssemblies))
        {
            string refName = referencedAssembly.Name;
            // Check for packages that users directly reference when defining activities/orchestrators
            // Microsoft.DurableTask.Worker: for non-function scenarios (console apps, etc.)
            // Microsoft.Azure.Functions.Worker.Extensions.DurableTask: for Azure Functions scenarios
            if (refName == "Microsoft.DurableTask.Worker" ||
                refName == "Microsoft.Azure.Functions.Worker.Extensions.DurableTask")
            {
                return true;
            }
        }

        return false;
    }

    static void ScanNamespaceForFunctions(
        INamespaceSymbol namespaceSymbol,
        KnownTypeSymbols knownSymbols,
        IMethodSymbol? taskActivityRunAsync,
        ConcurrentBag<string> activityNames,
        ConcurrentBag<string> orchestratorNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Scan types in this namespace
        foreach (INamedTypeSymbol typeSymbol in namespaceSymbol.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check for TaskActivity<TInput, TOutput> derived classes
            if (knownSymbols.TaskActivityBase != null &&
                taskActivityRunAsync != null &&
                !typeSymbol.IsAbstract &&
                ClassOverridesMethod(typeSymbol, taskActivityRunAsync))
            {
                activityNames.Add(typeSymbol.Name);
            }

            // Check for ITaskOrchestrator implementations (class-based orchestrators)
            if (knownSymbols.TaskOrchestratorInterface != null &&
                ImplementsInterface(typeSymbol, knownSymbols.TaskOrchestratorInterface))
            {
                orchestratorNames.Add(typeSymbol.Name);
            }

            // Check methods for [Function] + [ActivityTrigger] or [OrchestrationTrigger]
            foreach (ISymbol member in typeSymbol.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member is not IMethodSymbol methodSymbol)
                {
                    continue;
                }

                // Check for Activity defined via [ActivityTrigger]
                if (IsActivityMethod(methodSymbol, knownSymbols, out string functionName))
                {
                    activityNames.Add(functionName);
                }

                // Check for Orchestrator defined via [OrchestrationTrigger]
                if (IsOrchestratorMethod(methodSymbol, knownSymbols, out string orchestratorFunctionName))
                {
                    orchestratorNames.Add(orchestratorFunctionName);
                }
            }
        }

        // Recursively scan nested namespaces
        foreach (INamespaceSymbol nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            ScanNamespaceForFunctions(
                nestedNamespace,
                knownSymbols,
                taskActivityRunAsync,
                activityNames,
                orchestratorNames,
                cancellationToken);
        }
    }

    readonly struct FunctionInvocation(string name, SyntaxNode invocationSyntaxNode)
    {
        public string Name { get; } = name;

        public SyntaxNode InvocationSyntaxNode { get; } = invocationSyntaxNode;
    }
}
