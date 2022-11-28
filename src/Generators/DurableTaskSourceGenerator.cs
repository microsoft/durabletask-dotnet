﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    public class DurableTaskSourceGenerator : ISourceGenerator
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
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new DurableTaskSyntaxReceiver());
        }

        /// <inheritdoc/>
        public void Execute(GeneratorExecutionContext context)
        {
            // This generator also supports Durable Functions for .NET isolated, but we only generate Functions-specific
            // code if we find the Durable Functions extension listed in the set of referenced assembly names.
            bool isDurableFunctions = context.Compilation.ReferencedAssemblyNames.Any(
                assembly => assembly.Name.Equals("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", StringComparison.OrdinalIgnoreCase));

            // Enumerate all the activities in the project
            // the generator infrastructure will create a receiver and populate it
            // we can retrieve the populated instance via the context
            if (context.SyntaxContextReceiver is not DurableTaskSyntaxReceiver receiver)
            {
                // Unexpected receiver came back?
                return;
            }

            int found = receiver.Activities.Count + receiver.Orchestrators.Count + receiver.DurableFunctions.Count;
            if (found == 0)
            {
                // Didn't find anything
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
                foreach (DurableTaskTypeInfo orchestrator in receiver.Orchestrators)
                {
                    sourceBuilder.AppendLine($@"
        static readonly ITaskOrchestrator singleton{orchestrator.TaskName} = new {orchestrator.TypeName}();");
                }
            }

            foreach (DurableTaskTypeInfo orchestrator in receiver.Orchestrators)
            {
                if (isDurableFunctions)
                {
                    // Generate the function definition required to trigger orchestrators in Azure Functions
                    AddOrchestratorFunctionDeclaration(sourceBuilder, orchestrator);
                }

                AddOrchestratorCallMethod(sourceBuilder, orchestrator);
                AddSubOrchestratorCallMethod(sourceBuilder, orchestrator);
            }

            foreach (DurableTaskTypeInfo activity in receiver.Activities)
            {
                AddActivityCallMethod(sourceBuilder, activity);

                if (isDurableFunctions)
                {
                    // Generate the function definition required to trigger activities in Azure Functions
                    AddActivityFunctionDeclaration(sourceBuilder, activity);
                }
            }

            // Activity function triggers are supported for code-gen (but not orchestration triggers)
            IEnumerable<DurableFunction> activityTriggers = receiver.DurableFunctions.Where(
                df => df.Kind == DurableFunctionKind.Activity);
            foreach (DurableFunction function in activityTriggers)
            {
                AddActivityCallMethod(sourceBuilder, function);
            }

            if (isDurableFunctions)
            {
                if (receiver.Activities.Count > 0)
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
                    receiver.Orchestrators,
                    receiver.Activities);
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
        /// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
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
            ITaskActivity activity = ActivatorUtilities.CreateInstance<{activity.TypeName}>(executionContext.InstanceServices);
            TaskActivityContext context = new GeneratedActivityContext(""{activity.TaskName}"", instanceId);
            object? result = await activity.RunAsync(context, input);
            return ({activity.OutputType})result!;
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
            IEnumerable<DurableTaskTypeInfo> activities)
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

            sourceBuilder.AppendLine($@"
            return builder;
        }}");
        }

        class DurableTaskSyntaxReceiver : ISyntaxContextReceiver
        {
            readonly List<DurableTaskTypeInfo> orchestrators = new();
            readonly List<DurableTaskTypeInfo> activities = new();
            readonly List<DurableFunction> durableFunctions = new();

            public IReadOnlyList<DurableTaskTypeInfo> Orchestrators => this.orchestrators;
            public IReadOnlyList<DurableTaskTypeInfo> Activities => this.activities;
            public IReadOnlyList<DurableFunction> DurableFunctions => this.durableFunctions;

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                // Check for Azure Functions syntax
                if (context.Node is MethodDeclarationSyntax method &&
                    DurableFunction.TryParse(context.SemanticModel, method, out DurableFunction? function) &&
                    function != null)
                {
                    Debug.WriteLine($"Adding {function.Kind} function '{function.Name}'");
                    this.durableFunctions.Add(function);
                    return;
                }

                // Check for class-based syntax
                if (context.Node is not AttributeSyntax attribute)
                {
                    return;
                }

                ITypeSymbol? attributeType = context.SemanticModel.GetTypeInfo(attribute.Name).Type;
                if (attributeType?.ToString() != "Microsoft.DurableTask.DurableTaskAttribute")
                {
                    return;
                }

                if (attribute.Parent is not AttributeListSyntax list || list.Parent is not ClassDeclarationSyntax classDeclaration)
                {
                    // TODO: Issue a warning that the [DurableTask] attribute was found in a place it wasn't expected.
                    return;
                }

                // Verify that the attribute is being used on a non-abstract class
                if (classDeclaration.Modifiers.Any(SyntaxKind.AbstractKeyword))
                {
                    // TODO: Issue a warning that you can't use [DurableTask] on abstract classes
                    return;
                }

                if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not ITypeSymbol classType)
                {
                    // Invalid type declaration?
                    return;
                }

                string className = classType.ToDisplayString();

                List<DurableTaskTypeInfo>? taskList = null;
                INamedTypeSymbol? taskType = null;

                INamedTypeSymbol? baseType = classType.BaseType;
                while (baseType != null)
                {
                    if (baseType.ContainingAssembly.Name == "Microsoft.DurableTask.Abstractions")
                    {
                        if (baseType.Name == "TaskActivity")
                        {
                            taskList = this.activities;
                            taskType = baseType;
                            break;
                        }
                        else if (baseType.Name == "TaskOrchestrator")
                        {
                            taskList = this.orchestrators;
                            taskType = baseType;
                            break;
                        }
                    }

                    baseType = baseType.BaseType;
                }

                if (taskList == null || taskType == null)
                {
                    // TODO: Issue a warning that [DurableTask] can only be used with activity and orchestration-derived classes
                    return;
                }

                if (taskType.TypeParameters.Length <= 1)
                {
                    // We expect that the base class will always have at least two type parameters
                    return;
                }

                ITypeSymbol inputType = taskType.TypeArguments.First();
                ITypeSymbol outputType = taskType.TypeArguments.Last();

                // By default, the task name is the class name.
                string taskName = classType.Name; // TODO: What if the class has generic type parameters?
                if (attribute.ArgumentList?.Arguments.Count > 0)
                {
                    ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                    taskName = context.SemanticModel.GetConstantValue(expression).ToString();
                }

                taskList.Add(new DurableTaskTypeInfo(
                    className,
                    taskName,
                    inputType,
                    outputType));
            }
        }

        class DurableTaskTypeInfo
        {
            public DurableTaskTypeInfo(
                string taskType,
                string taskName,
                ITypeSymbol? inputType,
                ITypeSymbol? outputType)
            {
                this.TypeName = taskType;
                this.TaskName = taskName;
                this.InputType = GetRenderedTypeExpression(inputType);
                this.InputParameter = this.InputType + " input";
                if (this.InputType[this.InputType.Length - 1] == '?')
                {
                    this.InputParameter += " = default";
                }

                this.OutputType = GetRenderedTypeExpression(outputType);
            }

            public string TypeName { get; }
            public string TaskName { get; }
            public string InputType { get; }
            public string InputParameter { get; }
            public string OutputType { get; }

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