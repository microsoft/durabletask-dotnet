// Copyright (c) Microsoft Corporation.
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

            // Get common DI types that should not be treated as activity input
            INamedTypeSymbol? functionContextSymbol = context.Compilation.GetTypeByMetadataName("Microsoft.Azure.Functions.Worker.FunctionContext");

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

                    // If the parameter is FunctionContext, skip validation for this activity (it's a DI parameter, not real input)
                    if (functionContextSymbol != null && SymbolEqualityComparer.Default.Equals(inputParam.Type, functionContextSymbol))
                    {
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

                    // Check input type compatibility
                    if (!AreTypesCompatible(ctx.Compilation, invocation.InputType, activity.InputType))
                    {
                        string actual = invocation.InputType?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? "none";
                        string expected = activity.InputType?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? "none";
                        string activityName = invocation.Name;

                        Diagnostic diagnostic = RoslynExtensions.BuildDiagnostic(InputArgumentTypeMismatchRule, invocation.InvocationSyntaxNode, actual, expected, activityName);
                        ctx.ReportDiagnostic(diagnostic);
                    }

                    // Check output type compatibility
                    if (!AreTypesCompatible(ctx.Compilation, activity.OutputType, invocation.OutputType))
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

    /// <summary>
    /// Checks if the source type is compatible with (can be assigned to) the target type.
    /// This handles polymorphism, interface implementation, inheritance, and collection type compatibility.
    /// </summary>
    static bool AreTypesCompatible(Compilation compilation, ITypeSymbol? sourceType, ITypeSymbol? targetType)
    {
        // Both null = compatible
        if (sourceType == null && targetType == null)
        {
            return true;
        }

        // One is null, the other isn't = not compatible
        if (sourceType == null || targetType == null)
        {
            return false;
        }

        // Check if types are exactly equal
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return true;
        }

        // Check if source type can be converted to target type (handles inheritance, interface implementation, etc.)
        Conversion conversion = compilation.ClassifyConversion(sourceType, targetType);
        if (conversion.IsImplicit || conversion.IsIdentity)
        {
            return true;
        }

        // Special handling for collection types since ClassifyConversion doesn't always recognize
        // generic interface implementations (e.g., List<T> to IReadOnlyList<T>)
        if (IsCollectionTypeCompatible(sourceType, targetType))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the source collection type is compatible with the target collection type.
    /// Handles common scenarios like List to IReadOnlyList, arrays to IEnumerable, etc.
    /// </summary>
    static bool IsCollectionTypeCompatible(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        // Check if source is an array and target is a collection interface
        if (sourceType is IArrayTypeSymbol sourceArray && targetType is INamedTypeSymbol targetNamed)
        {
            return IsArrayCompatibleWithCollectionInterface(sourceArray, targetNamed);
        }

        // Both must be generic named types
        if (sourceType is not INamedTypeSymbol sourceNamed || targetType is not INamedTypeSymbol targetNamedType)
        {
            return false;
        }

        // Both must be generic types with the same type arguments
        if (!sourceNamed.IsGenericType || !targetNamedType.IsGenericType)
        {
            return false;
        }

        if (sourceNamed.TypeArguments.Length != targetNamedType.TypeArguments.Length)
        {
            return false;
        }

        // Check if type arguments are compatible (could be different but compatible types)
        for (int i = 0; i < sourceNamed.TypeArguments.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(sourceNamed.TypeArguments[i], targetNamedType.TypeArguments[i]))
            {
                // Type arguments must match exactly for collections (we don't support covariance/contravariance here)
                return false;
            }
        }

        // Check if source type implements or derives from target type
        // This handles: List<T> → IReadOnlyList<T>, List<T> → IEnumerable<T>, etc.
        return ImplementsInterface(sourceNamed, targetNamedType);
    }

    /// <summary>
    /// Checks if an array type is compatible with a collection interface.
    /// </summary>
    static bool IsArrayCompatibleWithCollectionInterface(IArrayTypeSymbol arrayType, INamedTypeSymbol targetInterface)
    {
        if (!targetInterface.IsGenericType || targetInterface.TypeArguments.Length != 1)
        {
            return false;
        }

        // Check if array element type matches the generic type argument
        if (!SymbolEqualityComparer.Default.Equals(arrayType.ElementType, targetInterface.TypeArguments[0]))
        {
            return false;
        }

        // Array implements: IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyCollection<T>, IReadOnlyList<T>
        string targetName = targetInterface.OriginalDefinition.ToDisplayString();
        return targetName == "System.Collections.Generic.IEnumerable<T>" ||
               targetName == "System.Collections.Generic.ICollection<T>" ||
               targetName == "System.Collections.Generic.IList<T>" ||
               targetName == "System.Collections.Generic.IReadOnlyCollection<T>" ||
               targetName == "System.Collections.Generic.IReadOnlyList<T>";
    }

    /// <summary>
    /// Checks if the source type implements the target interface.
    /// </summary>
    static bool ImplementsInterface(INamedTypeSymbol sourceType, INamedTypeSymbol targetInterface)
    {
        // Check all interfaces implemented by the source type
        foreach (INamedTypeSymbol @interface in sourceType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, targetInterface.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
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
