﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers.Activities;

/// <summary>
/// Analyzer that checks for mismatches between the input and output types of Activities invocations and their definitions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MatchingInputOutputTypeActivityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic ID for the diagnostic that reports when the input argument type of an Activity invocation does not match the input parameter type of the Activity definition.
    /// </summary>
    public const string InputArgumentTypeMismatchDiagnosticId = "DURABLE2001";

    /// <summary>
    /// The diagnostic ID for the diagnostic that reports when the output argument type of an Activity invocation does not match the return type of the Activity definition.
    /// </summary>
    public const string OutputArgumentTypeMismatchDiagnosticId = "DURABLE2002";

    static readonly LocalizableString InputArgumentTypeMismatchTitle = new LocalizableResourceString(nameof(Resources.InputArgumentTypeMismatchAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString InputArgumentTypeMismatchMessageFormat = new LocalizableResourceString(nameof(Resources.InputArgumentTypeMismatchAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly LocalizableString OutputArgumentTypeMismatchTitle = new LocalizableResourceString(nameof(Resources.OutputArgumentTypeMismatchAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString OutputArgumentTypeMismatchMessageFormat = new LocalizableResourceString(nameof(Resources.OutputArgumentTypeMismatchAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor InputArgumentTypeMismatchRule = new(
        InputArgumentTypeMismatchDiagnosticId,
        InputArgumentTypeMismatchTitle,
        InputArgumentTypeMismatchMessageFormat,
        AnalyzersCategories.Activity,
        DiagnosticSeverity.Warning,
        customTags: [WellKnownDiagnosticTags.CompilationEnd],
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor OutputArgumentTypeMismatchRule = new(
        OutputArgumentTypeMismatchDiagnosticId,
        OutputArgumentTypeMismatchTitle,
        OutputArgumentTypeMismatchMessageFormat,
        AnalyzersCategories.Activity,
        DiagnosticSeverity.Warning,
        customTags: [WellKnownDiagnosticTags.CompilationEnd],
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [InputArgumentTypeMismatchRule, OutputArgumentTypeMismatchRule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(context =>
        {
            KnownTypeSymbols knownSymbols = new(context.Compilation);

            if (knownSymbols.ActivityTriggerAttribute == null || knownSymbols.FunctionNameAttribute == null ||
                knownSymbols.DurableTaskRegistry == null || knownSymbols.TaskActivityBase == null ||
                knownSymbols.Task == null || knownSymbols.TaskT == null)
            {
                // symbols not available in this compilation, skip analysis
                return;
            }

            IMethodSymbol taskActivityRunAsync = knownSymbols.TaskActivityBase.GetMembers("RunAsync").OfType<IMethodSymbol>().Single();
            INamedTypeSymbol voidSymbol = context.Compilation.GetSpecialType(SpecialType.System_Void);

            // Search for Activity invocations
            ConcurrentBag<ActivityInvocation> invocations = [];
            context.RegisterOperationAction(
                ctx =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();

                    if (ctx.Operation is not IInvocationOperation invocationOperation)
                    {
                        return;
                    }

                    IMethodSymbol targetMethod = invocationOperation.TargetMethod;
                    if (!targetMethod.IsEqualTo(knownSymbols.TaskOrchestrationContext, "CallActivityAsync"))
                    {
                        return;
                    }

                    Debug.Assert(invocationOperation.Arguments.Length is 2 or 3, "CallActivityAsync has 2 or 3 parameters");
                    Debug.Assert(invocationOperation.Arguments[0].Parameter?.Name == "name", "First parameter of CallActivityAsync is name");
                    IArgumentOperation activityNameArgumentOperation = invocationOperation.Arguments[0];

                    // extracts the constant value from the argument (e.g.: it can be a nameof, string literal or const field)
                    Optional<object?> constant = ctx.Operation.SemanticModel!.GetConstantValue(activityNameArgumentOperation.Value.Syntax);
                    if (!constant.HasValue)
                    {
                        // not a constant value, we cannot correlate this invocation to an existent activity in compile time
                        return;
                    }

                    string activityName = constant.Value!.ToString();

                    // Try to extract the input argument from the invocation
                    ITypeSymbol? inputType = null;
                    IArgumentOperation? inputArgumentParameter = invocationOperation.Arguments.SingleOrDefault(a => a.Parameter?.Name == "input");
                    if (inputArgumentParameter != null && inputArgumentParameter.ArgumentKind != ArgumentKind.DefaultValue)
                    {
                        // if the argument is not null or a default value provided by the compiler, get the type before the conversion to object
                        TypeInfo inputTypeInfo = ctx.Operation.SemanticModel.GetTypeInfo(inputArgumentParameter.Value.Syntax, ctx.CancellationToken);
                        inputType = inputTypeInfo.Type;
                    }

                    // If the CallActivityAsync<TOutput> method is used, we extract the output type from TypeArguments
                    ITypeSymbol? outputType = targetMethod.OriginalDefinition.Arity == 1 && targetMethod.TypeArguments.Length == 1 ?
                        targetMethod.TypeArguments[0] : null;

                    invocations.Add(new ActivityInvocation()
                    {
                        Name = activityName,
                        InputType = inputType,
                        OutputType = outputType,
                        InvocationSyntaxNode = invocationOperation.Syntax,
                    });
                },
                OperationKind.Invocation);

            // Search for Durable Functions Activities definitions
            ConcurrentBag<ActivityDefinition> activities = [];
            context.RegisterSymbolAction(
                ctx =>
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();

                    if (ctx.Symbol is not IMethodSymbol methodSymbol)
                    {
                        return;
                    }

                    if (!methodSymbol.ContainsAttributeInAnyMethodArguments(knownSymbols.ActivityTriggerAttribute))
                    {
                        return;
                    }

                    if (!methodSymbol.TryGetSingleValueFromAttribute(knownSymbols.FunctionNameAttribute, out string functionName))
                    {
                        return;
                    }

                    IParameterSymbol? inputParam = methodSymbol.Parameters.SingleOrDefault(
                        p => p.GetAttributes().Any(a => knownSymbols.ActivityTriggerAttribute.Equals(a.AttributeClass, SymbolEqualityComparer.Default)));
                    if (inputParam == null)
                    {
                        // Azure Functions Activity methods must have an input parameter
                        return;
                    }

                    ITypeSymbol? inputType = inputParam.Type;

                    ITypeSymbol? outputType = methodSymbol.ReturnType;
                    if (outputType.Equals(voidSymbol, SymbolEqualityComparer.Default) ||
                        outputType.Equals(knownSymbols.Task, SymbolEqualityComparer.Default))
                    {
                        // If the method returns void or Task, we consider it as having no output
                        outputType = null;
                    }
                    else if (outputType.OriginalDefinition.Equals(knownSymbols.TaskT, SymbolEqualityComparer.Default) &&
                             outputType is INamedTypeSymbol outputNamedType)
                    {
                        // If the method is Task<T>, we consider T as the output type
                        Debug.Assert(outputNamedType.TypeArguments.Length == 1, "Task<T> has one type argument");
                        outputType = outputNamedType.TypeArguments[0];
                    }

                    activities.Add(new ActivityDefinition()
                    {
                        Name = functionName,
                        InputType = inputType,
                        OutputType = outputType,
                    });
                },
                SymbolKind.Method);

            // Search for TaskActivity<TInput, TOutput> definitions
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

                    // Check if the class has a method that overrides TaskActivity.RunAsync
                    IMethodSymbol? methodOverridingRunAsync = null;
                    INamedTypeSymbol? baseType = classSymbol; // start from the current class
                    while (baseType != null)
                    {
                        foreach (IMethodSymbol method in baseType.GetMembers().OfType<IMethodSymbol>())
                        {
                            if (SymbolEqualityComparer.Default.Equals(method.OverriddenMethod?.OriginalDefinition, taskActivityRunAsync))
                            {
                                methodOverridingRunAsync = method.OverriddenMethod;
                                break;
                            }
                        }

                        baseType = baseType.BaseType;
                    }

                    // TaskActivity.RunAsync method not found in the class hierarchy
                    if (methodOverridingRunAsync == null)
                    {
                        return;
                    }

                    // gets the closed constructed TaskActivity<TInput, TOutput> type, so we can extract TInput and TOutput
                    INamedTypeSymbol closedConstructedTaskActivity = methodOverridingRunAsync.ContainingType;
                    Debug.Assert(closedConstructedTaskActivity.TypeArguments.Length == 2, "TaskActivity has TInput and TOutput");

                    activities.Add(new ActivityDefinition()
                    {
                        Name = classSymbol.Name,
                        InputType = closedConstructedTaskActivity.TypeArguments[0],
                        OutputType = closedConstructedTaskActivity.TypeArguments[1],
                    });
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

                    if (!SymbolEqualityComparer.Default.Equals(invocation.Type, knownSymbols.DurableTaskRegistry))
                    {
                        return;
                    }

                    // there are 8 AddActivityFunc overloads, with combinations of Activity Name, TInput and TOutput
                    if (invocation.TargetMethod.Name != "AddActivityFunc")
                    {
                        return;
                    }

                    // all overloads have the parameter 'name', either as an Action or a Func
                    IArgumentOperation? activityNameArgumentOperation = invocation.Arguments.SingleOrDefault(a => a.Parameter!.Name == "name");
                    if (activityNameArgumentOperation == null)
                    {
                        return;
                    }

                    // extracts the constant value from the argument (e.g.: it can be a nameof, string literal or const field)
                    Optional<object?> constant = ctx.Operation.SemanticModel!.GetConstantValue(activityNameArgumentOperation.Value.Syntax);
                    if (!constant.HasValue)
                    {
                        // not a constant value, we cannot correlate this invocation to an existent activity in compile time
                        return;
                    }

                    string activityName = constant.Value!.ToString();

                    ITypeSymbol? inputType = invocation.TargetMethod.GetTypeArgumentByParameterName("TInput");
                    ITypeSymbol? outputType = invocation.TargetMethod.GetTypeArgumentByParameterName("TOutput");

                    activities.Add(new ActivityDefinition()
                    {
                        Name = activityName,
                        InputType = inputType,
                        OutputType = outputType,
                    });
                },
                OperationKind.Invocation);

            // At the end of the compilation, we correlate the invocations with the definitions
            context.RegisterCompilationEndAction(ctx =>
            {
                // index by name for faster lookup
                Dictionary<string, ActivityDefinition> activitiesByName = activities.ToDictionary(a => a.Name, a => a);

                foreach (ActivityInvocation invocation in invocations)
                {
                    if (!activitiesByName.TryGetValue(invocation.Name, out ActivityDefinition activity))
                    {
                        // Activity not found, we cannot correlate this invocation to an existent activity in compile time.
                        // We could add a diagnostic here if we want to enforce that, but while we experiment with this analyzer,
                        // we should prevent false positives.
                        continue;
                    }

                    if (!SymbolEqualityComparer.Default.Equals(invocation.InputType, activity.InputType))
                    {
                        string actual = invocation.InputType?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? "none";
                        string expected = activity.InputType?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? "none";
                        string activityName = invocation.Name;

                        Diagnostic diagnostic = RoslynExtensions.BuildDiagnostic(InputArgumentTypeMismatchRule, invocation.InvocationSyntaxNode, actual, expected, activityName);
                        ctx.ReportDiagnostic(diagnostic);
                    }

                    if (!SymbolEqualityComparer.Default.Equals(invocation.OutputType, activity.OutputType))
                    {
                        string actual = invocation.OutputType?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? "none";
                        string expected = activity.OutputType?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? "none";
                        string activityName = invocation.Name;

                        Diagnostic diagnostic = RoslynExtensions.BuildDiagnostic(OutputArgumentTypeMismatchRule, invocation.InvocationSyntaxNode, actual, expected, activityName);
                        ctx.ReportDiagnostic(diagnostic);
                    }
                }
            });
        });
    }

    struct ActivityInvocation
    {
        public string Name { get; set; }

        public ITypeSymbol? InputType { get; set; }

        public ITypeSymbol? OutputType { get; set; }

        public SyntaxNode InvocationSyntaxNode { get; set; }
    }

    struct ActivityDefinition
    {
        public string Name { get; set; }

        public ITypeSymbol? InputType { get; set; }

        public ITypeSymbol? OutputType { get; set; }
    }
}
