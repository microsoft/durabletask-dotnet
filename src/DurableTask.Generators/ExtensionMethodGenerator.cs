//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DurableTask.Generators
{
    [Generator]
    public class ExtensionMethodGenerator : ISourceGenerator
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

        public void Execute(GeneratorExecutionContext context)
        {
            // Enumerate all the activities in the project
            // the generator infrastructure will create a receiver and populate it
            // we can retrieve the populated instance via the context
            if (context.SyntaxContextReceiver is not MySyntaxReceiver syntaxReceiver)
            {
                // Unexpected receiver came back?
                return;
            }

            if (!syntaxReceiver.Activities.Any() && !syntaxReceiver.Orchestrators.Any())
            {
                // Didn't find anything
                return;
            }

            StringBuilder sourceBuilder = new(capacity: syntaxReceiver.Activities.Count * 1024);
            sourceBuilder.Append(@"// <generated />
#nullable enable

using System;
using System.Threading.Tasks;

namespace DurableTask
{
    public static class GeneratedDurableTaskExtensions
    {");

            foreach (DurableTaskTypeInfo orchestrator in syntaxReceiver.Orchestrators)
            {
                AddOrchestratorCallMethod(sourceBuilder, orchestrator);
                AddSubOrchestratorCallMethod(sourceBuilder, orchestrator);
            }

            foreach (DurableTaskTypeInfo activity in syntaxReceiver.Activities)
            {
                AddActivityCallMethod(sourceBuilder, activity);
            }

            AddRegistrationMethodForAllTasks(
                sourceBuilder,
                syntaxReceiver.Orchestrators,
                syntaxReceiver.Activities);

            sourceBuilder.AppendLine("    }").AppendLine("}");

            context.AddSource("GeneratedDurableTaskExtensions.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8, SourceHashAlgorithm.Sha256));
        }

        static void AddOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator)
        {
            sourceBuilder.AppendLine($@"
        /// <inheritdoc cref=""TaskHubClient.ScheduleNewOrchestrationInstanceAsync""/>
        public static Task<string> ScheduleNew{orchestrator.TaskName}InstanceAsync(
            this TaskHubClient client,
            string? instanceId = null,
            {orchestrator.InputType} input = default,
            DateTimeOffset? startTime = null)
        {{
            return client.ScheduleNewOrchestrationInstanceAsync(
                ""{orchestrator.TaskName}"",
                instanceId,
                input,
                startTime);
        }}");
        }

        static void AddSubOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator)
        {
            sourceBuilder.AppendLine($@"
        /// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
        public static Task<{orchestrator.OutputType}> Call{orchestrator.TaskName}Async(
            this TaskOrchestrationContext context,
            string? instanceId = null,
            {orchestrator.InputType} input = default,
            TaskOptions? options = null)
        {{
            return context.CallSubOrchestratorAsync<{orchestrator.OutputType}>(
                ""{orchestrator.TaskName}"",
                instanceId,
                input,
                options);
        }}");
        }

        static void AddActivityCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo activity)
        {
            sourceBuilder.AppendLine($@"
        public static Task<{activity.OutputType}> Call{activity.TaskName}Async(this TaskOrchestrationContext ctx, {activity.InputType} input, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{activity.OutputType}>(""{activity.TaskName}"", input, options);
        }}");
        }

        static void AddRegistrationMethodForAllTasks(
            StringBuilder sourceBuilder,
            IEnumerable<DurableTaskTypeInfo> orchestrators,
            IEnumerable<DurableTaskTypeInfo> activities)
        {
            sourceBuilder.Append($@"
        public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
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

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a factory that can create our custom syntax receiver
            context.RegisterForSyntaxNotifications(() => new MySyntaxReceiver());
        }

        class MySyntaxReceiver : ISyntaxContextReceiver
        {
            readonly List<DurableTaskTypeInfo> orchestrators = new();
            readonly List<DurableTaskTypeInfo> activities = new();

            public IReadOnlyList<DurableTaskTypeInfo> Orchestrators => this.orchestrators;
            public IReadOnlyList<DurableTaskTypeInfo> Activities => this.activities;

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                SyntaxNode syntaxNode = context.Node;
                if (syntaxNode is BaseListSyntax baseList && baseList.Parent is TypeDeclarationSyntax typeSyntax)
                {
                    // TODO: Validate that the type is not an abstract class

                    foreach (BaseTypeSyntax baseType in baseList.Types)
                    {
                        if (baseType.Type is not GenericNameSyntax genericName)
                        {
                            // This is not the type we're looking for
                            continue;
                        }

                        // TODO: Find a way to use the semantic model to do this so that we can support
                        //       custom base types that derive from our special base types.
                        List<DurableTaskTypeInfo> taskList;
                        if (genericName.Identifier.ValueText == "TaskActivityBase")
                        {
                            taskList = this.activities;
                        }
                        else if (genericName.Identifier.ValueText == "TaskOrchestratorBase")
                        {
                            taskList = this.orchestrators;
                        }
                        else
                        {
                            // This is not the type we're looking for
                            continue;
                        }

                        string typeName;
                        if (context.SemanticModel.GetDeclaredSymbol(typeSyntax) is not ISymbol typeSymbol)
                        {
                            // Invalid type declaration?
                            continue;
                        };

                        typeName = typeSymbol.ToDisplayString();

                        string inputType = "object";
                        string outputType = "object";
                        if (genericName.TypeArgumentList.Arguments.Count == 2)
                        {
                            if (Helpers.TryGetTypeName(context, genericName.TypeArgumentList.Arguments[0], out string? first))
                            {
                                inputType = first!;
                            }

                            if (Helpers.TryGetTypeName(context, genericName.TypeArgumentList.Arguments[1], out string? second))
                            {
                                outputType = second!;
                            }
                        }

                        // By default, the task name is the class name.
                        string taskName = typeSyntax.Identifier.ValueText; // TODO: What if the class has generic type parameters?;

                        // If a [DurableTask(name)] attribute is present, use that as the activity name.
                        foreach (AttributeSyntax attribute in typeSyntax.AttributeLists.SelectMany(list => list.Attributes))
                        {
                            if (Helpers.TryGetTypeName(context, attribute.Name, out string? attributeName) &&
                                attributeName == "DurableTask.DurableTaskAttribute" &&
                                attribute.ArgumentList?.Arguments.Count > 0)
                            {
                                ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                                taskName = context.SemanticModel.GetConstantValue(expression).ToString();
                                break;
                            }
                        }

                        taskList.Add(new DurableTaskTypeInfo(
                            typeName,
                            taskName,
                            inputType,
                            outputType));

                        break;
                    }
                }
            }
        }

        class DurableTaskTypeInfo
        {
            public DurableTaskTypeInfo(
                string taskType,
                string taskName,
                string inputType,
                string outputType)
            {
                this.TypeName = taskType;
                this.TaskName = taskName;
                this.InputType = inputType;
                this.OutputType = outputType;
            }

            public string TypeName { get; }
            public string TaskName { get; }
            public string InputType { get; }
            public string OutputType { get; }
        }
    }
}