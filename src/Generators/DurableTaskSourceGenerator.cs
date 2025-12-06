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

            // Create providers for Durable Functions
            IncrementalValuesProvider<DurableFunction> durableFunctions = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax,
                    transform: static (ctx, _) => GetDurableFunction(ctx))
                .Where(static func => func != null)!;

            // Collect all results and check if Durable Functions is referenced
            IncrementalValueProvider<(Compilation, ImmutableArray<DurableTaskTypeInfo>, ImmutableArray<DurableFunction>)> compilationAndTasks =
                durableTaskAttributes.Collect()
                    .Combine(durableFunctions.Collect())
                    .Combine(context.CompilationProvider)
                    .Select((x, _) => (x.Right, x.Left.Left, x.Left.Right));

            // Generate the source
            context.RegisterSourceOutput(compilationAndTasks, static (spc, source) => Execute(spc, source.Item1, source.Item2, source.Item3));
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

        static DurableFunction? GetDurableFunction(GeneratorSyntaxContext context)
        {
            MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;

            if (DurableFunction.TryParse(context.SemanticModel, method, out DurableFunction? function))
            {
                return function;
            }

            return null;
        }

        static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<DurableTaskTypeInfo> allTasks,
            ImmutableArray<DurableFunction> allFunctions)
        {
            if (allTasks.IsDefaultOrEmpty && allFunctions.IsDefaultOrEmpty)
            {
                return;
            }

            // This generator also supports Durable Functions for .NET isolated, but we only generate Functions-specific
            // code if we find the Durable Functions extension listed in the set of referenced assembly names.
            bool isDurableFunctions = compilation.ReferencedAssemblyNames.Any(
                assembly => assembly.Name.Equals("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", StringComparison.OrdinalIgnoreCase));

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

            int found = activities.Count + orchestrators.Count + entities.Count + allFunctions.Length;
            if (found == 0)
            {
                return;
            }

            StringBuilder sourceBuilder = new(capacity: found * 1024);
            sourceBuilder.Append(@"// <auto-generated/>
#nullable enable

using System;
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
                AddRegistrationMethodForAllTasks(
                    sourceBuilder,
                    orchestrators,
                    activities,
                    entities);
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
        public static Task<{activity.OutputType}> Call{activity.TaskName}Async(this TaskOrchestrationContext ctx, {activity.InputParameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{activity.OutputType}>(""{activity.TaskName}"", input, options);
        }}");
        }

        static void AddActivityCallMethod(StringBuilder sourceBuilder, DurableFunction activity)
        {
            sourceBuilder.AppendLine($@"
        public static Task<{activity.ReturnType}> Call{activity.Name}Async(this TaskOrchestrationContext ctx, {activity.Parameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{activity.ReturnType}>(""{activity.Name}"", {activity.Parameter.Name}, options);
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
    }
}
