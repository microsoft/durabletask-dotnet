// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.DurableTask.Generators.AzureFunctions;

namespace Microsoft.DurableTask.Generators
{
    /// <summary>
    /// Generator for DurableTask.
    /// </summary>
    [Generator]
    public class DurableTaskSourceGenerator : IIncrementalGenerator
    {
        /* Example input:
         * 
         * [DurableTask("MyActivity")]
         * class MyActivity : TaskActivityBase<CustomType, string>
         * {
         *     public Task<string> RunAsync(CustomType input)
         *     {
         *         string instanceId = this.Context.InstanceId;
         *         // ...
         *         return input.ToString();
         *     }
         * }
         * 
         * Example output:
         * 
         * public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
         * {
         *     return ctx.CallActivityAsync("MyActivity", input, options);
         * }
         */

        /// <summary>
        /// Diagnostic ID for invalid task names.
        /// </summary>
        const string InvalidTaskNameDiagnosticId = "DURABLE3001";

        /// <summary>
        /// Diagnostic ID for invalid event names.
        /// </summary>
        const string InvalidEventNameDiagnosticId = "DURABLE3002";

        static readonly DiagnosticDescriptor InvalidTaskNameRule = new(
            InvalidTaskNameDiagnosticId,
            title: "Invalid task name",
            messageFormat: "The task name '{0}' is not a valid C# identifier. Task names must start with a letter or underscore and contain only letters, digits, and underscores.",
            category: "DurableTask.Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        static readonly DiagnosticDescriptor InvalidEventNameRule = new(
            InvalidEventNameDiagnosticId,
            title: "Invalid event name",
            messageFormat: "The event name '{0}' is not a valid C# identifier. Event names must start with a letter or underscore and contain only letters, digits, and underscores.",
            category: "DurableTask.Design",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Create providers for DurableTask attributes
            IncrementalValuesProvider<DurableTaskTypeInfo> durableTaskAttributes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is AttributeSyntax,
                    transform: static (ctx, _) => GetDurableTaskTypeInfo(ctx))
                .Where(static info => info != null)!;

            // Create providers for DurableEvent attributes
            IncrementalValuesProvider<DurableEventTypeInfo> durableEventAttributes = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is AttributeSyntax,
                    transform: static (ctx, _) => GetDurableEventTypeInfo(ctx))
                .Where(static info => info != null)!;

            // Create providers for Durable Functions
            IncrementalValuesProvider<DurableFunction> durableFunctions = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax,
                    transform: static (ctx, _) => GetDurableFunction(ctx))
                .Where(static func => func != null)!;

            // Get the project type configuration from MSBuild properties
            IncrementalValueProvider<string?> projectTypeProvider = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) =>
                {
                    provider.GlobalOptions.TryGetValue("build_property.DurableTaskGeneratorProjectType", out string? projectType);
                    return projectType;
                });

            // Collect all results and check if Durable Functions is referenced
            IncrementalValueProvider<(Compilation, ImmutableArray<DurableTaskTypeInfo>, ImmutableArray<DurableEventTypeInfo>, ImmutableArray<DurableFunction>, string?)> compilationAndTasks =
                durableTaskAttributes.Collect()
                    .Combine(durableEventAttributes.Collect())
                    .Combine(durableFunctions.Collect())
                    .Combine(context.CompilationProvider)
                    .Combine(projectTypeProvider)
                    // Roslyn's IncrementalValueProvider.Combine creates nested tuple pairs: ((Left, Right), Right)
                    // After multiple .Combine() calls, we unpack the nested structure:
                    // x.Right                     = projectType (string?)
                    // x.Left.Right                = Compilation
                    // x.Left.Left.Left.Left       = DurableTaskAttributes (orchestrators, activities, entities)
                    // x.Left.Left.Left.Right      = DurableEventAttributes (events)
                    // x.Left.Left.Right           = DurableFunctions (Azure Functions metadata)
                    .Select((x, _) => (x.Left.Right, x.Left.Left.Left.Left, x.Left.Left.Left.Right, x.Left.Left.Right, x.Right));

            // Generate the source
            context.RegisterSourceOutput(compilationAndTasks, static (spc, source) => Execute(spc, source.Item1, source.Item2, source.Item3, source.Item4, source.Item5));
        }

        static DurableTaskTypeInfo? GetDurableTaskTypeInfo(GeneratorSyntaxContext context)
        {
            AttributeSyntax attribute = (AttributeSyntax)context.Node;

            ITypeSymbol? attributeType = context.SemanticModel.GetTypeInfo(attribute.Name).Type;
            if (attributeType?.ToString() != "Microsoft.DurableTask.DurableTaskAttribute")
            {
                return null;
            }

            if (attribute.Parent is not AttributeListSyntax list || list.Parent is not ClassDeclarationSyntax classDeclaration)
            {
                return null;
            }

            // Verify that the attribute is being used on a non-abstract class
            if (classDeclaration.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                return null;
            }

            if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not ITypeSymbol classType)
            {
                return null;
            }

            string className = classType.ToDisplayString();
            string classNamespace = classType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : classType.ContainingNamespace.ToDisplayString();
            INamedTypeSymbol? taskType = null;
            DurableTaskKind kind = DurableTaskKind.Orchestrator;

            INamedTypeSymbol? baseType = classType.BaseType;
            while (baseType != null)
            {
                if (baseType.ContainingAssembly.Name == "Microsoft.DurableTask.Abstractions")
                {
                    if (baseType.Name == "TaskActivity")
                    {
                        taskType = baseType;
                        kind = DurableTaskKind.Activity;
                        break;
                    }
                    else if (baseType.Name == "TaskOrchestrator")
                    {
                        taskType = baseType;
                        kind = DurableTaskKind.Orchestrator;
                        break;
                    }
                    else if (baseType.Name == "TaskEntity")
                    {
                        taskType = baseType;
                        kind = DurableTaskKind.Entity;
                        break;
                    }
                }

                baseType = baseType.BaseType;
            }

            // TaskEntity has 1 type parameter (TState), while TaskActivity and TaskOrchestrator have 2 (TInput, TOutput)
            if (taskType == null)
            {
                return null;
            }

            if (kind == DurableTaskKind.Entity)
            {
                // Entity only has a single TState type parameter
                if (taskType.TypeParameters.Length < 1)
                {
                    return null;
                }
            }
            else
            {
                // Orchestrator and Activity have TInput and TOutput type parameters
                if (taskType.TypeParameters.Length <= 1)
                {
                    return null;
                }
            }

            ITypeSymbol? inputType = kind == DurableTaskKind.Entity ? null : taskType.TypeArguments.First();
            ITypeSymbol? outputType = kind == DurableTaskKind.Entity ? null : taskType.TypeArguments.Last();

            string taskName = classType.Name;
            Location? taskNameLocation = null;
            if (attribute.ArgumentList?.Arguments.Count > 0)
            {
                ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                taskName = context.SemanticModel.GetConstantValue(expression).ToString();
                taskNameLocation = expression.GetLocation();
            }

            return new DurableTaskTypeInfo(className, classNamespace, taskName, inputType, outputType, kind, taskNameLocation);
        }

        static DurableEventTypeInfo? GetDurableEventTypeInfo(GeneratorSyntaxContext context)
        {
            AttributeSyntax attribute = (AttributeSyntax)context.Node;

            ITypeSymbol? attributeType = context.SemanticModel.GetTypeInfo(attribute.Name).Type;
            if (attributeType?.ToString() != "Microsoft.DurableTask.DurableEventAttribute")
            {
                return null;
            }

            // DurableEventAttribute can be applied to both class and struct (record)
            TypeDeclarationSyntax? typeDeclaration = attribute.Parent?.Parent as TypeDeclarationSyntax;
            if (typeDeclaration == null)
            {
                return null;
            }

            // Verify that the attribute is being used on a non-abstract type
            if (typeDeclaration.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                return null;
            }

            if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not ITypeSymbol eventType)
            {
                return null;
            }

            string eventName = eventType.Name;
            Location? eventNameLocation = null;

            if (attribute.ArgumentList?.Arguments.Count > 0)
            {
                ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                Optional<object?> constantValue = context.SemanticModel.GetConstantValue(expression);
                if (constantValue.HasValue && constantValue.Value is string value)
                {
                    eventName = value;
                    eventNameLocation = expression.GetLocation();
                }
            }

            string eventNamespace = eventType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : eventType.ContainingNamespace.ToDisplayString();

            return new DurableEventTypeInfo(eventName, eventNamespace, eventType, eventNameLocation);
        }

        static DurableFunction? GetDurableFunction(GeneratorSyntaxContext context)
        {
            MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;

            if (DurableFunction.TryParse(context.SemanticModel, method, out DurableFunction? function))
            {
                return function;
            }

            return null;
        }

        /// <summary>
        /// Checks if a name is a valid C# identifier.
        /// </summary>
        /// <param name="name">The name to validate.</param>
        /// <returns>True if the name is a valid C# identifier, false otherwise.</returns>
        static bool IsValidCSharpIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // Use Roslyn's built-in identifier validation
            return SyntaxFacts.IsValidIdentifier(name);
        }

        static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<DurableTaskTypeInfo> allTasks,
            ImmutableArray<DurableEventTypeInfo> allEvents,
            ImmutableArray<DurableFunction> allFunctions,
            string? projectType)
        {
            if (allTasks.IsDefaultOrEmpty && allEvents.IsDefaultOrEmpty && allFunctions.IsDefaultOrEmpty)
            {
                return;
            }

            // Validate task names and report diagnostics for invalid identifiers
            foreach (DurableTaskTypeInfo task in allTasks)
            {
                if (!IsValidCSharpIdentifier(task.TaskName))
                {
                    Location location = task.TaskNameLocation ?? Location.None;
                    Diagnostic diagnostic = Diagnostic.Create(InvalidTaskNameRule, location, task.TaskName);
                    context.ReportDiagnostic(diagnostic);
                }
            }

            // Validate event names and report diagnostics for invalid identifiers
            foreach (DurableEventTypeInfo eventInfo in allEvents.Where(e => !IsValidCSharpIdentifier(e.EventName)))
            {
                Location location = eventInfo.EventNameLocation ?? Location.None;
                Diagnostic diagnostic = Diagnostic.Create(InvalidEventNameRule, location, eventInfo.EventName);
                context.ReportDiagnostic(diagnostic);
            }

            // Determine if we should generate Durable Functions specific code
            bool isDurableFunctions = DetermineIsDurableFunctions(compilation, allFunctions, projectType);

            // Separate tasks into orchestrators, activities, and entities
            // Skip tasks with invalid names to avoid generating invalid code
            List<DurableTaskTypeInfo> orchestrators = new();
            List<DurableTaskTypeInfo> activities = new();
            List<DurableTaskTypeInfo> entities = new();

            IEnumerable<DurableTaskTypeInfo> validTasks = allTasks
                .Where(task => IsValidCSharpIdentifier(task.TaskName));

            foreach (DurableTaskTypeInfo task in validTasks)
            {
                if (task.IsActivity)
                {
                    activities.Add(task);
                }
                else if (task.IsEntity)
                {
                    entities.Add(task);
                }
                else
                {
                    orchestrators.Add(task);
                }
            }

            // Filter out events with invalid names
            List<DurableEventTypeInfo> validEvents = allEvents
                .Where(eventInfo => IsValidCSharpIdentifier(eventInfo.EventName))
                .ToList();

            int found = activities.Count + orchestrators.Count + entities.Count + validEvents.Count + allFunctions.Length;
            if (found == 0)
            {
                return;
            }

            // Group tasks by namespace. Tasks in the global namespace use "Microsoft.DurableTask" for backward compatibility.
            Dictionary<string, List<DurableTaskTypeInfo>> tasksByNamespace = new();
            foreach (DurableTaskTypeInfo task in orchestrators.Concat(activities).Concat(entities))
            {
                string targetNamespace = string.IsNullOrEmpty(task.Namespace) ? "Microsoft.DurableTask" : task.Namespace;
                if (!tasksByNamespace.TryGetValue(targetNamespace, out List<DurableTaskTypeInfo>? list))
                {
                    list = new List<DurableTaskTypeInfo>();
                    tasksByNamespace[targetNamespace] = list;
                }

                list.Add(task);
            }

            // Group events by namespace. Events in the global namespace use "Microsoft.DurableTask".
            Dictionary<string, List<DurableEventTypeInfo>> eventsByNamespace = new();
            foreach (DurableEventTypeInfo eventInfo in validEvents)
            {
                string targetNamespace = string.IsNullOrEmpty(eventInfo.Namespace) ? "Microsoft.DurableTask" : eventInfo.Namespace;
                if (!eventsByNamespace.TryGetValue(targetNamespace, out List<DurableEventTypeInfo>? list))
                {
                    list = new List<DurableEventTypeInfo>();
                    eventsByNamespace[targetNamespace] = list;
                }

                list.Add(eventInfo);
            }

            // Collect all distinct namespaces
            HashSet<string> allNamespaces = new(tasksByNamespace.Keys);
            foreach (string ns in eventsByNamespace.Keys)
            {
                allNamespaces.Add(ns);
            }

            // Activity function triggers from DurableFunction go into Microsoft.DurableTask namespace
            List<DurableFunction> activityTriggers = allFunctions.Where(
                df => df.Kind == DurableFunctionKind.Activity).ToList();
            if (activityTriggers.Count > 0)
            {
                allNamespaces.Add("Microsoft.DurableTask");
            }

            // Registration method always goes in Microsoft.DurableTask
            bool needsRegistrationMethod = !isDurableFunctions && (orchestrators.Count > 0 || activities.Count > 0 || entities.Count > 0);
            if (needsRegistrationMethod)
            {
                allNamespaces.Add("Microsoft.DurableTask");
            }

            StringBuilder sourceBuilder = new(capacity: found * 1024);
            sourceBuilder.Append(@"// <auto-generated/>
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Internal;");

            if (isDurableFunctions)
            {
                sourceBuilder.Append(@"
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;");
            }

            // Sort namespaces so "Microsoft.DurableTask" comes last for consistent output
            List<string> sortedNamespaces = allNamespaces
                .OrderBy(ns => ns == "Microsoft.DurableTask" ? 1 : 0)
                .ThenBy(ns => ns, StringComparer.Ordinal)
                .ToList();

            foreach (string targetNamespace in sortedNamespaces)
            {
                tasksByNamespace.TryGetValue(targetNamespace, out List<DurableTaskTypeInfo>? tasksInNamespace);
                eventsByNamespace.TryGetValue(targetNamespace, out List<DurableEventTypeInfo>? eventsInNamespace);

                List<DurableTaskTypeInfo> orchestratorsInNs = tasksInNamespace?.Where(t => t.IsOrchestrator).ToList() ?? new();
                List<DurableTaskTypeInfo> activitiesInNs = tasksInNamespace?.Where(t => t.IsActivity).ToList() ?? new();
                List<DurableTaskTypeInfo> entitiesInNs = tasksInNamespace?.Where(t => t.IsEntity).ToList() ?? new();
                bool isMicrosoftDurableTask = targetNamespace == "Microsoft.DurableTask";

                // Check if there's any content to generate for this namespace
                bool hasOrchestratorMethods = orchestratorsInNs.Count > 0;
                bool hasActivityMethods = activitiesInNs.Count > 0;
                bool hasEntityFunctions = isDurableFunctions && entitiesInNs.Count > 0;
                bool hasActivityTriggers = isMicrosoftDurableTask && activityTriggers.Count > 0;
                bool hasEvents = eventsInNamespace != null && eventsInNamespace.Count > 0;
                bool hasRegistration = isMicrosoftDurableTask && needsRegistrationMethod;

                if (!hasOrchestratorMethods && !hasActivityMethods && !hasEntityFunctions
                    && !hasActivityTriggers && !hasEvents && !hasRegistration)
                {
                    continue;
                }

                sourceBuilder.Append($@"

namespace {targetNamespace}
{{
    public static class GeneratedDurableTaskExtensions
    {{");
                if (isDurableFunctions)
                {
                    // Generate a singleton orchestrator object instance that can be reused for all invocations.
                    foreach (DurableTaskTypeInfo orchestrator in orchestratorsInNs)
                    {
                        sourceBuilder.AppendLine($@"
        static readonly ITaskOrchestrator singleton{orchestrator.TaskName} = new {SimplifyTypeName(orchestrator.TypeName, targetNamespace)}();");
                    }
                }

                foreach (DurableTaskTypeInfo orchestrator in orchestratorsInNs)
                {
                    if (isDurableFunctions)
                    {
                        AddOrchestratorFunctionDeclaration(sourceBuilder, orchestrator, targetNamespace);
                    }

                    AddOrchestratorCallMethod(sourceBuilder, orchestrator, targetNamespace);
                    AddSubOrchestratorCallMethod(sourceBuilder, orchestrator, targetNamespace);
                }

                foreach (DurableTaskTypeInfo activity in activitiesInNs)
                {
                    AddActivityCallMethod(sourceBuilder, activity, targetNamespace);

                    if (isDurableFunctions)
                    {
                        AddActivityFunctionDeclaration(sourceBuilder, activity, targetNamespace);
                    }
                }

                foreach (DurableTaskTypeInfo entity in entitiesInNs)
                {
                    if (isDurableFunctions)
                    {
                        AddEntityFunctionDeclaration(sourceBuilder, entity, targetNamespace);
                    }
                }

                // Activity function triggers from DurableFunction always go in Microsoft.DurableTask
                if (isMicrosoftDurableTask)
                {
                    foreach (DurableFunction function in activityTriggers)
                    {
                        AddActivityCallMethod(sourceBuilder, function);
                    }
                }

                // Generate WaitFor/Send methods for events in this namespace
                if (eventsInNamespace != null)
                {
                    foreach (DurableEventTypeInfo eventInfo in eventsInNamespace)
                    {
                        AddEventWaitMethod(sourceBuilder, eventInfo, targetNamespace);
                        AddEventSendMethod(sourceBuilder, eventInfo, targetNamespace);
                    }
                }

                if (isDurableFunctions)
                {
                    if (activitiesInNs.Count > 0)
                    {
                        AddGeneratedActivityContextClass(sourceBuilder);
                    }
                }

                // Registration method goes in Microsoft.DurableTask namespace only
                if (isMicrosoftDurableTask && needsRegistrationMethod)
                {
                    AddRegistrationMethodForAllTasks(
                        sourceBuilder,
                        orchestrators,
                        activities,
                        entities);
                }

                sourceBuilder.AppendLine("    }").AppendLine("}");
            }

            context.AddSource("GeneratedDurableTaskExtensions.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8, SourceHashAlgorithm.Sha256));
        }

        /// <summary>
        /// Determines whether the current project should be treated as an Azure Functions-based Durable Functions project.
        /// </summary>
        /// <param name="compilation">The Roslyn compilation for the project, used to inspect referenced assemblies.</param>
        /// <param name="allFunctions">The collection of discovered Durable Functions triggers in the project.</param>
        /// <param name="projectType">
        /// An optional project type hint. When set to <c>"Functions"</c> or <c>"Standalone"</c>, this value takes precedence
        /// over automatic detection. Any other value (including <c>"Auto"</c>) falls back to auto-detection.
        /// </param>
        /// <returns>
        /// <c>true</c> if the project is determined to be a Durable Functions (Azure Functions) project; otherwise, <c>false</c>.
        /// </returns>
        static bool DetermineIsDurableFunctions(Compilation compilation, ImmutableArray<DurableFunction> allFunctions, string? projectType)
        {
            // Check if the user has explicitly configured the project type
            if (!string.IsNullOrWhiteSpace(projectType))
            {
                // Explicit configuration takes precedence
                if (projectType!.Equals("Functions", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                else if (projectType.Equals("Standalone", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                // If "Auto" or unrecognized value, fall through to auto-detection
            }

            // Auto-detect based on the presence of Azure Functions trigger attributes
            // If we found any methods with OrchestrationTrigger, ActivityTrigger, or EntityTrigger attributes,
            // then this is a Durable Functions project
            if (!allFunctions.IsDefaultOrEmpty)
            {
                return true;
            }

            // Fallback: check if Durable Functions assembly is referenced
            // This handles edge cases where the project references the assembly but hasn't defined triggers yet
            return compilation.ReferencedAssemblyNames.Any(
                assembly => assembly.Name.Equals("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Simplifies a fully qualified type name relative to a target namespace.
        /// Types in the same namespace are returned without the namespace prefix.
        /// </summary>
        static string SimplifyTypeName(string fullyQualifiedTypeName, string targetNamespace)
        {
            if (string.IsNullOrEmpty(targetNamespace))
            {
                return fullyQualifiedTypeName;
            }

            if (fullyQualifiedTypeName.StartsWith(targetNamespace + ".", StringComparison.Ordinal))
            {
                return fullyQualifiedTypeName.Substring(targetNamespace.Length + 1);
            }

            return fullyQualifiedTypeName;
        }

        static void AddOrchestratorFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator, string targetNamespace)
        {
            string inputType = orchestrator.GetInputTypeForNamespace(targetNamespace);
            string outputType = orchestrator.GetOutputTypeForNamespace(targetNamespace);

            sourceBuilder.AppendLine($@"
        [Function(nameof({orchestrator.TaskName}))]
        public static Task<{outputType}> {orchestrator.TaskName}([OrchestrationTrigger] TaskOrchestrationContext context)
        {{
            return singleton{orchestrator.TaskName}.RunAsync(context, context.GetInput<{inputType}>())
                .ContinueWith(t => ({outputType})(t.Result ?? default({outputType})!), TaskContinuationOptions.ExecuteSynchronously);
        }}");
        }

        static void AddOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator, string targetNamespace)
        {
            string inputType = orchestrator.GetInputTypeForNamespace(targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            string simplifiedTypeName = SimplifyTypeName(orchestrator.TypeName, targetNamespace);

            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Schedules a new instance of the <see cref=""{simplifiedTypeName}""/> orchestrator.
        /// </summary>
        /// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
        public static Task<string> ScheduleNew{orchestrator.TaskName}InstanceAsync(
            this IOrchestrationSubmitter client, {inputParameter}, StartOrchestrationOptions? options = null)
        {{
            return client.ScheduleNewOrchestrationInstanceAsync(""{orchestrator.TaskName}"", input, options);
        }}");
        }

        static void AddSubOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator, string targetNamespace)
        {
            string inputType = orchestrator.GetInputTypeForNamespace(targetNamespace);
            string outputType = orchestrator.GetOutputTypeForNamespace(targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            string simplifiedTypeName = SimplifyTypeName(orchestrator.TypeName, targetNamespace);

            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Calls the <see cref=""{simplifiedTypeName}""/> sub-orchestrator.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
        public static Task<{outputType}> Call{orchestrator.TaskName}Async(
            this TaskOrchestrationContext context, {inputParameter}, TaskOptions? options = null)
        {{
            return context.CallSubOrchestratorAsync<{outputType}>(""{orchestrator.TaskName}"", input, options);
        }}");
        }

        static void AddActivityCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo activity, string targetNamespace)
        {
            string inputType = activity.GetInputTypeForNamespace(targetNamespace);
            string outputType = activity.GetOutputTypeForNamespace(targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            string simplifiedTypeName = SimplifyTypeName(activity.TypeName, targetNamespace);

            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Calls the <see cref=""{simplifiedTypeName}""/> activity.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
        public static Task<{outputType}> Call{activity.TaskName}Async(this TaskOrchestrationContext ctx, {inputParameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{outputType}>(""{activity.TaskName}"", input, options);
        }}");
        }

        static void AddActivityCallMethod(StringBuilder sourceBuilder, DurableFunction activity)
        {
            if (activity.ReturnsVoid)
            {
                sourceBuilder.AppendLine($@"
        /// <summary>
        /// Calls the <see cref=""{activity.FullTypeName}""/> activity.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
        public static Task Call{activity.Name}Async(this TaskOrchestrationContext ctx, {activity.Parameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync(""{activity.Name}"", {activity.Parameter.Name}, options);
        }}");
            }
            else
            {
                sourceBuilder.AppendLine($@"
        /// <summary>
        /// Calls the <see cref=""{activity.FullTypeName}""/> activity.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
        public static Task<{activity.ReturnType}> Call{activity.Name}Async(this TaskOrchestrationContext ctx, {activity.Parameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{activity.ReturnType}>(""{activity.Name}"", {activity.Parameter.Name}, options);
        }}");
            }
        }

        static void AddEventWaitMethod(StringBuilder sourceBuilder, DurableEventTypeInfo eventInfo, string targetNamespace)
        {
            string typeName = SimplifyTypeName(eventInfo.TypeName, targetNamespace);

            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Waits for an external event of type <see cref=""{typeName}""/>.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.WaitForExternalEvent{{T}}(string, CancellationToken)""/>
        public static Task<{typeName}> WaitFor{eventInfo.EventName}Async(this TaskOrchestrationContext context, CancellationToken cancellationToken = default)
        {{
            return context.WaitForExternalEvent<{typeName}>(""{eventInfo.EventName}"", cancellationToken);
        }}");
        }

        static void AddEventSendMethod(StringBuilder sourceBuilder, DurableEventTypeInfo eventInfo, string targetNamespace)
        {
            string typeName = SimplifyTypeName(eventInfo.TypeName, targetNamespace);

            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Sends an external event of type <see cref=""{typeName}""/> to another orchestration instance.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.SendEvent(string, string, object)""/>
        public static void Send{eventInfo.EventName}(this TaskOrchestrationContext context, string instanceId, {typeName} eventData)
        {{
            context.SendEvent(instanceId, ""{eventInfo.EventName}"", eventData);
        }}");
        }

        static void AddActivityFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo activity, string targetNamespace)
        {
            string inputType = activity.GetInputTypeForNamespace(targetNamespace);
            string outputType = activity.GetOutputTypeForNamespace(targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            string simplifiedActivityTypeName = SimplifyTypeName(activity.TypeName, targetNamespace);
            // GeneratedActivityContext is a generated class that we use for each generated activity trigger definition.
            // Note that the second "instanceId" parameter is populated via the Azure Functions binding context.
            sourceBuilder.AppendLine($@"
        [Function(nameof({activity.TaskName}))]
        public static async Task<{outputType}> {activity.TaskName}([ActivityTrigger] {inputParameter}, string instanceId, FunctionContext executionContext)
        {{
            ITaskActivity activity = ActivatorUtilities.GetServiceOrCreateInstance<{simplifiedActivityTypeName}>(executionContext.InstanceServices);
            TaskActivityContext context = new GeneratedActivityContext(""{activity.TaskName}"", instanceId);
            object? result = await activity.RunAsync(context, input);
            return ({outputType})result!;
        }}");
        }

        static void AddEntityFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo entity, string targetNamespace)
        {
            string simplifiedEntityTypeName = SimplifyTypeName(entity.TypeName, targetNamespace);

            // Generate the entity trigger function that dispatches to the entity implementation.
            sourceBuilder.AppendLine($@"
        [Function(nameof({entity.TaskName}))]
        public static Task {entity.TaskName}([EntityTrigger] TaskEntityDispatcher dispatcher)
        {{
            return dispatcher.DispatchAsync<{simplifiedEntityTypeName}>();
        }}");
        }

        /// <summary>
        /// Adds a custom ITaskActivityContext implementation used by code generated from <see cref="AddActivityFunctionDeclaration"/>.
        /// </summary>
        static void AddGeneratedActivityContextClass(StringBuilder sourceBuilder)
        {
            // NOTE: Any breaking changes to ITaskActivityContext need to be reflected here as well.
            sourceBuilder.AppendLine(GetGeneratedActivityContextCode());
        }

        /// <summary>
        /// Gets the generated activity context code.
        /// </summary>
        /// <returns>The generated activity context code.</returns>
        public static string GetGeneratedActivityContextCode() => $@"
        sealed class GeneratedActivityContext : TaskActivityContext
        {{
            public GeneratedActivityContext(TaskName name, string instanceId)
            {{
                this.Name = name;
                this.InstanceId = instanceId;
            }}

            public override TaskName Name {{ get; }}

            public override string InstanceId {{ get; }}
        }}";

        static void AddRegistrationMethodForAllTasks(
            StringBuilder sourceBuilder,
            IEnumerable<DurableTaskTypeInfo> orchestrators,
            IEnumerable<DurableTaskTypeInfo> activities,
            IEnumerable<DurableTaskTypeInfo> entities)
        {
            // internal so it does not conflict with other projects with this generated file.
            sourceBuilder.Append($@"
        internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
        {{");

            foreach (DurableTaskTypeInfo taskInfo in orchestrators)
            {
                sourceBuilder.Append($@"
            builder.AddOrchestrator<{taskInfo.TypeName}>();");
            }

            foreach (DurableTaskTypeInfo taskInfo in activities)
            {
                sourceBuilder.Append($@"
            builder.AddActivity<{taskInfo.TypeName}>();");
            }

            foreach (DurableTaskTypeInfo taskInfo in entities)
            {
                sourceBuilder.Append($@"
            builder.AddEntity<{taskInfo.TypeName}>();");
            }

            sourceBuilder.AppendLine($@"
            return builder;
        }}");
        }

        enum DurableTaskKind
        {
            Orchestrator,
            Activity,
            Entity
        }

        class DurableTaskTypeInfo
        {
            public DurableTaskTypeInfo(
                string taskType,
                string taskNamespace,
                string taskName,
                ITypeSymbol? inputType,
                ITypeSymbol? outputType,
                DurableTaskKind kind,
                Location? taskNameLocation = null)
            {
                this.TypeName = taskType;
                this.Namespace = taskNamespace;
                this.TaskName = taskName;
                this.Kind = kind;
                this.TaskNameLocation = taskNameLocation;
                this.InputTypeSymbol = inputType;
                this.OutputTypeSymbol = outputType;
            }

            public string TypeName { get; }
            public string Namespace { get; }
            public string TaskName { get; }
            public DurableTaskKind Kind { get; }
            public Location? TaskNameLocation { get; }
            ITypeSymbol? InputTypeSymbol { get; }
            ITypeSymbol? OutputTypeSymbol { get; }

            public bool IsActivity => this.Kind == DurableTaskKind.Activity;

            public bool IsOrchestrator => this.Kind == DurableTaskKind.Orchestrator;

            public bool IsEntity => this.Kind == DurableTaskKind.Entity;

            /// <summary>
            /// Gets the rendered input type expression relative to the specified namespace.
            /// </summary>
            public string GetInputTypeForNamespace(string targetNamespace)
            {
                return GetRenderedTypeExpressionForNamespace(this.InputTypeSymbol, targetNamespace);
            }

            /// <summary>
            /// Gets the rendered output type expression relative to the specified namespace.
            /// </summary>
            public string GetOutputTypeForNamespace(string targetNamespace)
            {
                return GetRenderedTypeExpressionForNamespace(this.OutputTypeSymbol, targetNamespace);
            }

            static string GetRenderedTypeExpressionForNamespace(ITypeSymbol? symbol, string targetNamespace)
            {
                if (symbol == null)
                {
                    return "object";
                }

                string expression = symbol.ToDisplayString();

                // Simplify System types (e.g., System.String -> String, System.Int32 -> int)
                if (expression.StartsWith("System.", StringComparison.Ordinal)
                    && symbol.ContainingNamespace.ToDisplayString() == "System")
                {
                    expression = expression.Substring("System.".Length);
                }
                // Simplify types in the same namespace
                else if (!string.IsNullOrEmpty(targetNamespace)
                    && symbol.ContainingNamespace.ToDisplayString() == targetNamespace)
                {
                    expression = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                }

                return expression;
            }
        }

        class DurableEventTypeInfo
        {
            public DurableEventTypeInfo(string eventName, string eventNamespace, ITypeSymbol eventType, Location? eventNameLocation = null)
            {
                this.TypeName = GetRenderedTypeExpression(eventType);
                this.Namespace = eventNamespace;
                this.EventName = eventName;
                this.EventNameLocation = eventNameLocation;
            }

            public string TypeName { get; }
            public string Namespace { get; }
            public string EventName { get; }
            public Location? EventNameLocation { get; }

            static string GetRenderedTypeExpression(ITypeSymbol? symbol)
            {
                if (symbol == null)
                {
                    return "object";
                }

                string expression = symbol.ToString();
                if (expression.StartsWith("System.", StringComparison.Ordinal)
                    && symbol.ContainingNamespace.Name == "System")
                {
                    expression = expression.Substring("System.".Length);
                }

                return expression;
            }
        }
    }
}
