// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
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
            if (attribute.ArgumentList?.Arguments.Count > 0)
            {
                ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                taskName = context.SemanticModel.GetConstantValue(expression).ToString();
            }

            return new DurableTaskTypeInfo(className, taskName, inputType, outputType, kind);
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

            if (attribute.ArgumentList?.Arguments.Count > 0)
            {
                ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                Optional<object?> constantValue = context.SemanticModel.GetConstantValue(expression);
                if (constantValue.HasValue && constantValue.Value is string value)
                {
                    eventName = value;
                }
            }

            return new DurableEventTypeInfo(eventName, eventType);
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
        /// Determines if code generation should be skipped for Durable Functions scenarios.
        /// Returns true if only entities exist and the runtime supports native class-based invocation,
        /// since entities don't generate extension methods and the runtime handles their registration.
        /// </summary>
        static bool ShouldSkipGenerationForDurableFunctions(
            bool supportsNativeClassBasedInvocation,
            List<DurableTaskTypeInfo> orchestrators,
            List<DurableTaskTypeInfo> activities,
            ImmutableArray<DurableEventTypeInfo> allEvents,
            ImmutableArray<DurableFunction> allFunctions)
        {
            return supportsNativeClassBasedInvocation &&
                orchestrators.Count == 0 &&
                activities.Count == 0 &&
                allEvents.Length == 0 &&
                allFunctions.Length == 0;
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

            // This generator also supports Durable Functions for .NET isolated, but we only generate Functions-specific
            // code if we find the Durable Functions extension listed in the set of referenced assembly names.
            bool isDurableFunctions = compilation.ReferencedAssemblyNames.Any(
                assembly => assembly.Name.Equals("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", StringComparison.OrdinalIgnoreCase));

            // Check if the Durable Functions extension version supports native class-based invocation.
            // This feature was introduced in PR #3229: https://github.com/Azure/azure-functions-durable-extension/pull/3229
            // For the isolated worker extension (Microsoft.Azure.Functions.Worker.Extensions.DurableTask),
            // native class-based invocation support was added in version 1.11.0.
            bool supportsNativeClassBasedInvocation = false;
            if (isDurableFunctions)
            {
                var durableFunctionsAssembly = compilation.ReferencedAssemblyNames.FirstOrDefault(
                    assembly => assembly.Name.Equals("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", StringComparison.OrdinalIgnoreCase));
                
                if (durableFunctionsAssembly != null && durableFunctionsAssembly.Version >= new Version(1, 11, 0))
                {
                    supportsNativeClassBasedInvocation = true;
                }
            }

            // Separate tasks into orchestrators, activities, and entities
            List<DurableTaskTypeInfo> orchestrators = new();
            List<DurableTaskTypeInfo> activities = new();
            List<DurableTaskTypeInfo> entities = new();

            foreach (DurableTaskTypeInfo task in allTasks)
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

            int found = activities.Count + orchestrators.Count + entities.Count + allEvents.Length + allFunctions.Length;
            if (found == 0)
            {
                return;
            }

            // With Durable Functions' native support for class-based invocations (PR #3229, v3.8.0+),
            // we no longer generate [Function] definitions for class-based tasks when the runtime
            // supports native invocation. If we have ONLY entities (no orchestrators, no activities,
            // no events, no method-based functions), then there's nothing to generate for those
            // scenarios since entities don't have extension methods.
            if (ShouldSkipGenerationForDurableFunctions(supportsNativeClassBasedInvocation, orchestrators, activities, allEvents, allFunctions))
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

            // Generate singleton orchestrator instances for older Durable Functions versions
            // that don't have native class-based invocation support
            if (isDurableFunctions && !supportsNativeClassBasedInvocation)
            {
                foreach (DurableTaskTypeInfo orchestrator in orchestrators)
                {
                    sourceBuilder.AppendLine($@"
        static readonly ITaskOrchestrator singleton{orchestrator.TaskName} = new {orchestrator.TypeName}();");
                }
            }

            // Note: When targeting Azure Functions (Durable Functions scenarios) with native support
            // for class-based invocations (PR #3229, v3.8.0+), we no longer generate [Function] attribute
            // definitions for class-based orchestrators, activities, and entities (i.e., classes that
            // implement ITaskOrchestrator, ITaskActivity, or ITaskEntity and are decorated with the
            // [DurableTask] attribute). The Durable Functions runtime handles function registration
            // for these types automatically in those scenarios. For older versions of Durable Functions
            // (prior to v3.8.0) or non-Durable Functions scenarios (for example, ASP.NET Core using
            // the Durable Task Scheduler), we continue to generate [Function] definitions.
            // We always generate extension methods for type-safe invocation.

            foreach (DurableTaskTypeInfo orchestrator in orchestrators)
            {
                // Only generate [Function] definitions for Durable Functions if the runtime doesn't
                // support native class-based invocation (versions prior to v3.8.0)
                if (isDurableFunctions && !supportsNativeClassBasedInvocation)
                {
                    AddOrchestratorFunctionDeclaration(sourceBuilder, orchestrator);
                }

                AddOrchestratorCallMethod(sourceBuilder, orchestrator);
                AddSubOrchestratorCallMethod(sourceBuilder, orchestrator);
            }

            foreach (DurableTaskTypeInfo activity in activities)
            {
                AddActivityCallMethod(sourceBuilder, activity);

                // Only generate [Function] definitions for Durable Functions if the runtime doesn't
                // support native class-based invocation (versions prior to v3.8.0)
                if (isDurableFunctions && !supportsNativeClassBasedInvocation)
                {
                    AddActivityFunctionDeclaration(sourceBuilder, activity);
                }
            }

            foreach (DurableTaskTypeInfo entity in entities)
            {
                // Only generate [Function] definitions for Durable Functions if the runtime doesn't
                // support native class-based invocation (versions prior to v3.8.0)
                if (isDurableFunctions && !supportsNativeClassBasedInvocation)
                {
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
            foreach (DurableEventTypeInfo eventInfo in allEvents)
            {
                AddEventWaitMethod(sourceBuilder, eventInfo);
                AddEventSendMethod(sourceBuilder, eventInfo);
            }

            // Note: The GeneratedActivityContext class is only needed for older versions of
            // Durable Functions (prior to v3.8.0) that don't have native class-based invocation support.
            // For v3.8.0+, the runtime handles class-based invocations natively.
            if (isDurableFunctions && !supportsNativeClassBasedInvocation)
            {
                if (activities.Count > 0)
                {
                    // Functions-specific helper class, which is only needed when
                    // using the class-based syntax with older Durable Functions versions.
                    AddGeneratedActivityContextClass(sourceBuilder);
                }
            }
            else if (!isDurableFunctions)
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
                DurableTaskKind kind)
            {
                this.TypeName = taskType;
                this.TaskName = taskName;
                this.Kind = kind;

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
            public DurableEventTypeInfo(string eventName, ITypeSymbol eventType)
            {
                this.TypeName = GetRenderedTypeExpression(eventType);
                this.EventName = eventName;
            }

            public string TypeName { get; }
            public string EventName { get; }

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
