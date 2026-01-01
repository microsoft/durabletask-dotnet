// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Linq;
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
        const string InvalidTaskNameDiagnosticId = "DURABLE1001";

        /// <summary>
        /// Diagnostic ID for invalid event names.
        /// </summary>
        const string InvalidEventNameDiagnosticId = "DURABLE1002";

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

            // Collect all results and check if Durable Functions is referenced
            IncrementalValueProvider<(Compilation, ImmutableArray<DurableTaskTypeInfo>, ImmutableArray<DurableEventTypeInfo>, ImmutableArray<DurableFunction>)> compilationAndTasks =
                durableTaskAttributes.Collect()
                    .Combine(durableEventAttributes.Collect())
                    .Combine(durableFunctions.Collect())
                    .Combine(context.CompilationProvider)
                    // Roslyn's IncrementalValueProvider.Combine creates nested tuple pairs: ((Left, Right), Right)
                    // After multiple .Combine() calls, we unpack the nested structure:
                    // x.Right                = Compilation
                    // x.Left.Left.Left       = DurableTaskAttributes (orchestrators, activities, entities)
                    // x.Left.Left.Right      = DurableEventAttributes (events)
                    // x.Left.Right           = DurableFunctions (Azure Functions metadata)
                    .Select((x, _) => (x.Right, x.Left.Left.Left, x.Left.Left.Right, x.Left.Right));

            // Generate the source
            context.RegisterSourceOutput(compilationAndTasks, static (spc, source) => Execute(spc, source.Item1, source.Item2, source.Item3, source.Item4));
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

            return new DurableTaskTypeInfo(className, taskName, inputType, outputType, kind, taskNameLocation);
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

            return new DurableEventTypeInfo(eventName, eventType, eventNameLocation);
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
            ImmutableArray<DurableFunction> allFunctions)
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
            foreach (DurableEventTypeInfo eventInfo in allEvents)
            {
                if (!IsValidCSharpIdentifier(eventInfo.EventName))
                {
                    Location location = eventInfo.EventNameLocation ?? Location.None;
                    Diagnostic diagnostic = Diagnostic.Create(InvalidEventNameRule, location, eventInfo.EventName);
                    context.ReportDiagnostic(diagnostic);
                }
            }

            // This generator also supports Durable Functions for .NET isolated, but we only generate Functions-specific
            // code if we find the Durable Functions extension listed in the set of referenced assembly names.
            bool isDurableFunctions = compilation.ReferencedAssemblyNames.Any(
                assembly => assembly.Name.Equals("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", StringComparison.OrdinalIgnoreCase));

            // Separate tasks into orchestrators, activities, and entities
            // Skip tasks with invalid names to avoid generating invalid code
            List<DurableTaskTypeInfo> orchestrators = new();
            List<DurableTaskTypeInfo> activities = new();
            List<DurableTaskTypeInfo> entities = new();

            foreach (DurableTaskTypeInfo task in allTasks)
            {
                // Skip tasks with invalid names
                if (!IsValidCSharpIdentifier(task.TaskName))
                {
                    continue;
                }

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

            StringBuilder sourceBuilder = new(capacity: found * 1024);
            sourceBuilder.Append(@"// <auto-generated/>
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DurableTask.Internal;");

            if (isDurableFunctions)
            {
                sourceBuilder.Append(@"
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;");
            }

            sourceBuilder.Append(@"

namespace Microsoft.DurableTask
{
    public static class GeneratedDurableTaskExtensions
    {");
            if (isDurableFunctions)
            {
                // Generate a singleton orchestrator object instance that can be reused for all invocations.
                foreach (DurableTaskTypeInfo orchestrator in orchestrators)
                {
                    sourceBuilder.AppendLine($@"
        static readonly ITaskOrchestrator singleton{orchestrator.TaskName} = new {orchestrator.TypeName}();");
                }
            }

            foreach (DurableTaskTypeInfo orchestrator in orchestrators)
            {
                if (isDurableFunctions)
                {
                    // Generate the function definition required to trigger orchestrators in Azure Functions
                    AddOrchestratorFunctionDeclaration(sourceBuilder, orchestrator);
                }

                AddOrchestratorCallMethod(sourceBuilder, orchestrator);
                AddSubOrchestratorCallMethod(sourceBuilder, orchestrator);
            }

            foreach (DurableTaskTypeInfo activity in activities)
            {
                AddActivityCallMethod(sourceBuilder, activity);

                if (isDurableFunctions)
                {
                    // Generate the function definition required to trigger activities in Azure Functions
                    AddActivityFunctionDeclaration(sourceBuilder, activity);
                }
            }

            foreach (DurableTaskTypeInfo entity in entities)
            {
                if (isDurableFunctions)
                {
                    // Generate the function definition required to trigger entities in Azure Functions
                    AddEntityFunctionDeclaration(sourceBuilder, entity);
                }
            }

            // Activity function triggers are supported for code-gen (but not orchestration triggers)
            IEnumerable<DurableFunction> activityTriggers = allFunctions.Where(
                df => df.Kind == DurableFunctionKind.Activity);
            foreach (DurableFunction function in activityTriggers)
            {
                AddActivityCallMethod(sourceBuilder, function);
            }

            // Generate WaitFor{EventName}Async methods for each event type
            foreach (DurableEventTypeInfo eventInfo in validEvents)
            {
                AddEventWaitMethod(sourceBuilder, eventInfo);
                AddEventSendMethod(sourceBuilder, eventInfo);
            }

            if (isDurableFunctions)
            {
                if (activities.Count > 0)
                {
                    // Functions-specific helper class, which is only needed when
                    // using the class-based syntax.
                    AddGeneratedActivityContextClass(sourceBuilder);
                }
            }
            else
            {
                // ASP.NET Core-specific service registration methods
                // Only generate if there are actually tasks to register
                if (orchestrators.Count > 0 || activities.Count > 0 || entities.Count > 0)
                {
                    AddRegistrationMethodForAllTasks(
                        sourceBuilder,
                        orchestrators,
                        activities,
                        entities);   
                }
            }

            sourceBuilder.AppendLine("    }").AppendLine("}");

            context.AddSource("GeneratedDurableTaskExtensions.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8, SourceHashAlgorithm.Sha256));
        }

        static void AddOrchestratorFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator)
        {
            sourceBuilder.AppendLine($@"
        [Function(nameof({orchestrator.TaskName}))]
        public static Task<{orchestrator.OutputType}> {orchestrator.TaskName}([OrchestrationTrigger] TaskOrchestrationContext context)
        {{
            return singleton{orchestrator.TaskName}.RunAsync(context, context.GetInput<{orchestrator.InputType}>())
                .ContinueWith(t => ({orchestrator.OutputType})(t.Result ?? default({orchestrator.OutputType})!), TaskContinuationOptions.ExecuteSynchronously);
        }}");
        }

        static void AddOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator)
        {
            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Schedules a new instance of the <see cref=""{orchestrator.TypeName}""/> orchestrator.
        /// </summary>
        /// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
        public static Task<string> ScheduleNew{orchestrator.TaskName}InstanceAsync(
            this IOrchestrationSubmitter client, {orchestrator.InputParameter}, StartOrchestrationOptions? options = null)
        {{
            return client.ScheduleNewOrchestrationInstanceAsync(""{orchestrator.TaskName}"", input, options);
        }}");
        }

        static void AddSubOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator)
        {
            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Calls the <see cref=""{orchestrator.TypeName}""/> sub-orchestrator.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
        public static Task<{orchestrator.OutputType}> Call{orchestrator.TaskName}Async(
            this TaskOrchestrationContext context, {orchestrator.InputParameter}, TaskOptions? options = null)
        {{
            return context.CallSubOrchestratorAsync<{orchestrator.OutputType}>(""{orchestrator.TaskName}"", input, options);
        }}");
        }

        static void AddActivityCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo activity)
        {
            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Calls the <see cref=""{activity.TypeName}""/> activity.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
        public static Task<{activity.OutputType}> Call{activity.TaskName}Async(this TaskOrchestrationContext ctx, {activity.InputParameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{activity.OutputType}>(""{activity.TaskName}"", input, options);
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

        static void AddEventWaitMethod(StringBuilder sourceBuilder, DurableEventTypeInfo eventInfo)
        {
            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Waits for an external event of type <see cref=""{eventInfo.TypeName}""/>.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.WaitForExternalEvent{{T}}(string, CancellationToken)""/>
        public static Task<{eventInfo.TypeName}> WaitFor{eventInfo.EventName}Async(this TaskOrchestrationContext context, CancellationToken cancellationToken = default)
        {{
            return context.WaitForExternalEvent<{eventInfo.TypeName}>(""{eventInfo.EventName}"", cancellationToken);
        }}");
        }

        static void AddEventSendMethod(StringBuilder sourceBuilder, DurableEventTypeInfo eventInfo)
        {
            sourceBuilder.AppendLine($@"
        /// <summary>
        /// Sends an external event of type <see cref=""{eventInfo.TypeName}""/> to another orchestration instance.
        /// </summary>
        /// <inheritdoc cref=""TaskOrchestrationContext.SendEvent(string, string, object)""/>
        public static void Send{eventInfo.EventName}(this TaskOrchestrationContext context, string instanceId, {eventInfo.TypeName} eventData)
        {{
            context.SendEvent(instanceId, ""{eventInfo.EventName}"", eventData);
        }}");
        }

        static void AddActivityFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo activity)
        {
            // GeneratedActivityContext is a generated class that we use for each generated activity trigger definition.
            // Note that the second "instanceId" parameter is populated via the Azure Functions binding context.
            sourceBuilder.AppendLine($@"
        [Function(nameof({activity.TaskName}))]
        public static async Task<{activity.OutputType}> {activity.TaskName}([ActivityTrigger] {activity.InputParameter}, string instanceId, FunctionContext executionContext)
        {{
            ITaskActivity activity = ActivatorUtilities.GetServiceOrCreateInstance<{activity.TypeName}>(executionContext.InstanceServices);
            TaskActivityContext context = new GeneratedActivityContext(""{activity.TaskName}"", instanceId);
            object? result = await activity.RunAsync(context, input);
            return ({activity.OutputType})result!;
        }}");
        }

        static void AddEntityFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo entity)
        {
            // Generate the entity trigger function that dispatches to the entity implementation.
            sourceBuilder.AppendLine($@"
        [Function(nameof({entity.TaskName}))]
        public static Task {entity.TaskName}([EntityTrigger] TaskEntityDispatcher dispatcher)
        {{
            return dispatcher.DispatchAsync<{entity.TypeName}>();
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
                string taskName,
                ITypeSymbol? inputType,
                ITypeSymbol? outputType,
                DurableTaskKind kind,
                Location? taskNameLocation = null)
            {
                this.TypeName = taskType;
                this.TaskName = taskName;
                this.Kind = kind;
                this.TaskNameLocation = taskNameLocation;

                // Entities only have a state type parameter, not input/output
                if (kind == DurableTaskKind.Entity)
                {
                    this.InputType = string.Empty;
                    this.InputParameter = string.Empty;
                    this.OutputType = string.Empty;
                }
                else
                {
                    this.InputType = GetRenderedTypeExpression(inputType);
                    this.InputParameter = this.InputType + " input";
                    if (this.InputType[this.InputType.Length - 1] == '?')
                    {
                        this.InputParameter += " = default";
                    }

                    this.OutputType = GetRenderedTypeExpression(outputType);
                }
            }

            public string TypeName { get; }
            public string TaskName { get; }
            public string InputType { get; }
            public string InputParameter { get; }
            public string OutputType { get; }
            public DurableTaskKind Kind { get; }
            public Location? TaskNameLocation { get; }

            public bool IsActivity => this.Kind == DurableTaskKind.Activity;

            public bool IsOrchestrator => this.Kind == DurableTaskKind.Orchestrator;

            public bool IsEntity => this.Kind == DurableTaskKind.Entity;

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

        class DurableEventTypeInfo
        {
            public DurableEventTypeInfo(string eventName, ITypeSymbol eventType, Location? eventNameLocation = null)
            {
                this.TypeName = GetRenderedTypeExpression(eventType);
                this.EventName = eventName;
                this.EventNameLocation = eventNameLocation;
            }

            public string TypeName { get; }
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
