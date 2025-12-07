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
            
            // Get namespace, handling global namespace specially
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
            if (attribute.ArgumentList?.Arguments.Count > 0)
            {
                ExpressionSyntax expression = attribute.ArgumentList.Arguments[0].Expression;
                taskName = context.SemanticModel.GetConstantValue(expression).ToString();
            }

            return new DurableTaskTypeInfo(className, classNamespace, taskName, inputType, outputType, kind);
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

            // Group tasks by namespace
            // For tasks in the global namespace (empty string), use "Microsoft.DurableTask" for backward compatibility
            Dictionary<string, List<DurableTaskTypeInfo>> tasksByNamespace = new();
            
            foreach (DurableTaskTypeInfo task in allTasks)
            {
                string targetNamespace = string.IsNullOrEmpty(task.Namespace) ? "Microsoft.DurableTask" : task.Namespace;
                
                if (!tasksByNamespace.TryGetValue(targetNamespace, out List<DurableTaskTypeInfo>? tasksInNamespace))
                {
                    tasksInNamespace = new List<DurableTaskTypeInfo>();
                    tasksByNamespace[targetNamespace] = tasksInNamespace;
                }
                
                tasksInNamespace.Add(task);
            }

            // Generate a separate class for each namespace
            StringBuilder sourceBuilder = new(capacity: found * 1024);
            sourceBuilder.Append(@"// <auto-generated/>
#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Internal;");

            if (isDurableFunctions)
            {
                sourceBuilder.Append(@"
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;");
            }

            sourceBuilder.AppendLine();

            // Activity function triggers are supported for code-gen (but not orchestration triggers)
            IEnumerable<DurableFunction> activityTriggers = allFunctions.Where(
                df => df.Kind == DurableFunctionKind.Activity);

            // For non-Functions, we need to add registration methods to Microsoft.DurableTask namespace
            bool needsRegistrationBlock = !isDurableFunctions && (orchestrators.Count > 0 || activities.Count > 0 || entities.Count > 0);

            // Generate extension classes grouped by namespace
            foreach (KeyValuePair<string, List<DurableTaskTypeInfo>> namespaceGroup in tasksByNamespace)
            {
                string targetNamespace = namespaceGroup.Key;
                List<DurableTaskTypeInfo> tasksInNamespace = namespaceGroup.Value;

                List<DurableTaskTypeInfo> orchestratorsInNamespace = tasksInNamespace.Where(t => t.IsOrchestrator).ToList();
                List<DurableTaskTypeInfo> activitiesInNamespace = tasksInNamespace.Where(t => t.IsActivity).ToList();
                List<DurableTaskTypeInfo> entitiesInNamespace = tasksInNamespace.Where(t => t.IsEntity).ToList();

                // Check if there's actually any content to generate for this namespace
                bool hasOrchestratorMethods = orchestratorsInNamespace.Count > 0;
                bool hasActivityMethods = activitiesInNamespace.Count > 0;
                bool hasEntityFunctions = isDurableFunctions && entitiesInNamespace.Count > 0;
                bool hasActivityTriggers = targetNamespace == "Microsoft.DurableTask" && activityTriggers.Any();
                bool hasRegistrationMethod = !isDurableFunctions && targetNamespace == "Microsoft.DurableTask" && needsRegistrationBlock;

                // Skip this namespace block if there's nothing to generate
                if (!hasOrchestratorMethods && !hasActivityMethods && !hasEntityFunctions && !hasActivityTriggers && !hasRegistrationMethod)
                {
                    continue;
                }

                sourceBuilder.AppendLine();
                sourceBuilder.AppendLine($"namespace {targetNamespace}");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine("    public static class GeneratedDurableTaskExtensions");
                sourceBuilder.AppendLine("    {");

                if (isDurableFunctions)
                {
                    // Generate a singleton orchestrator object instance that can be reused for all invocations.
                    foreach (DurableTaskTypeInfo orchestrator in orchestratorsInNamespace)
                    {
                        string simplifiedTypeName = SimplifyTypeNameForNamespace(orchestrator.TypeName, targetNamespace);
                        sourceBuilder.AppendLine($@"        static readonly ITaskOrchestrator singleton{orchestrator.TaskName} = new {simplifiedTypeName}();");
                    }
                }

                foreach (DurableTaskTypeInfo orchestrator in orchestratorsInNamespace)
                {
                    if (isDurableFunctions)
                    {
                        // Generate the function definition required to trigger orchestrators in Azure Functions
                        AddOrchestratorFunctionDeclaration(sourceBuilder, orchestrator, targetNamespace);
                    }

                    AddOrchestratorCallMethod(sourceBuilder, orchestrator, targetNamespace);
                    AddSubOrchestratorCallMethod(sourceBuilder, orchestrator, targetNamespace);
                }

                foreach (DurableTaskTypeInfo activity in activitiesInNamespace)
                {
                    AddActivityCallMethod(sourceBuilder, activity, targetNamespace);

                    if (isDurableFunctions)
                    {
                        // Generate the function definition required to trigger activities in Azure Functions
                        AddActivityFunctionDeclaration(sourceBuilder, activity, targetNamespace);
                    }
                }

                foreach (DurableTaskTypeInfo entity in entitiesInNamespace)
                {
                    if (isDurableFunctions)
                    {
                        // Generate the function definition required to trigger entities in Azure Functions
                        AddEntityFunctionDeclaration(sourceBuilder, entity, targetNamespace);
                    }
                }

                // Add activity triggers from DurableFunction to Microsoft.DurableTask namespace only
                if (targetNamespace == "Microsoft.DurableTask" && activityTriggers.Any())
                {
                    foreach (DurableFunction function in activityTriggers)
                    {
                        AddActivityCallMethod(sourceBuilder, function);
                    }
                }

                if (isDurableFunctions)
                {
                    if (activitiesInNamespace.Count > 0)
                    {
                        // Functions-specific helper class, which is only needed when
                        // using the class-based syntax.
                        AddGeneratedActivityContextClass(sourceBuilder);
                    }
                }
                else
                {
                    // ASP.NET Core-specific service registration methods - add to Microsoft.DurableTask namespace only
                    if (targetNamespace == "Microsoft.DurableTask" && needsRegistrationBlock)
                    {
                        AddRegistrationMethodForAllTasks(
                            sourceBuilder,
                            orchestrators,
                            activities,
                            entities);
                        needsRegistrationBlock = false; // Mark as added
                    }
                }

                sourceBuilder.AppendLine("    }");
                sourceBuilder.AppendLine("}");
            }

            // If we still need to add activity triggers or registration methods and they haven't been added yet
            // (because there's no Microsoft.DurableTask namespace block), create one now
            if (activityTriggers.Any() && !tasksByNamespace.ContainsKey("Microsoft.DurableTask"))
            {
                sourceBuilder.AppendLine();
                sourceBuilder.AppendLine("namespace Microsoft.DurableTask");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine("    public static class GeneratedDurableTaskExtensions");
                sourceBuilder.AppendLine("    {");

                foreach (DurableFunction function in activityTriggers)
                {
                    AddActivityCallMethod(sourceBuilder, function);
                }

                sourceBuilder.AppendLine("    }");
                sourceBuilder.AppendLine("}");
            }

            if (needsRegistrationBlock && !tasksByNamespace.ContainsKey("Microsoft.DurableTask"))
            {
                sourceBuilder.AppendLine();
                sourceBuilder.AppendLine("namespace Microsoft.DurableTask");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine("    public static class GeneratedDurableTaskExtensions");
                sourceBuilder.AppendLine("    {");

                AddRegistrationMethodForAllTasks(
                    sourceBuilder,
                    orchestrators,
                    activities,
                    entities);

                sourceBuilder.AppendLine("    }");
                sourceBuilder.AppendLine("}");
            }

            context.AddSource("GeneratedDurableTaskExtensions.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8, SourceHashAlgorithm.Sha256));

        }

        static string SimplifyTypeNameForNamespace(string fullyQualifiedTypeName, string targetNamespace)
        {
            if (fullyQualifiedTypeName.StartsWith(targetNamespace + ".", StringComparison.Ordinal))
            {
                return fullyQualifiedTypeName.Substring(targetNamespace.Length + 1);
            }

            return fullyQualifiedTypeName;
        }

        static void AddOrchestratorFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator, string targetNamespace)
        {
            string inputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(orchestrator.InputTypeSymbol, targetNamespace);
            string outputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(orchestrator.OutputTypeSymbol, targetNamespace);

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
            string inputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(orchestrator.InputTypeSymbol, targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            sourceBuilder.AppendLine($@"
        /// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
        public static Task<string> ScheduleNew{orchestrator.TaskName}InstanceAsync(
            this IOrchestrationSubmitter client, {inputParameter}, StartOrchestrationOptions? options = null)
        {{
            return client.ScheduleNewOrchestrationInstanceAsync(""{orchestrator.TaskName}"", input, options);
        }}");
        }

        static void AddSubOrchestratorCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo orchestrator, string targetNamespace)
        {
            string inputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(orchestrator.InputTypeSymbol, targetNamespace);
            string outputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(orchestrator.OutputTypeSymbol, targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            sourceBuilder.AppendLine($@"
        /// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
        public static Task<{outputType}> Call{orchestrator.TaskName}Async(
            this TaskOrchestrationContext context, {inputParameter}, TaskOptions? options = null)
        {{
            return context.CallSubOrchestratorAsync<{outputType}>(""{orchestrator.TaskName}"", input, options);
        }}");
        }

        static void AddActivityCallMethod(StringBuilder sourceBuilder, DurableTaskTypeInfo activity, string targetNamespace)
        {
            string inputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(activity.InputTypeSymbol, targetNamespace);
            string outputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(activity.OutputTypeSymbol, targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            sourceBuilder.AppendLine($@"
        public static Task<{outputType}> Call{activity.TaskName}Async(this TaskOrchestrationContext ctx, {inputParameter}, TaskOptions? options = null)
        {{
            return ctx.CallActivityAsync<{outputType}>(""{activity.TaskName}"", input, options);
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

        static void AddActivityFunctionDeclaration(StringBuilder sourceBuilder, DurableTaskTypeInfo activity, string targetNamespace)
        {
            string inputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(activity.InputTypeSymbol, targetNamespace);
            string outputType = DurableTaskTypeInfo.GetRenderedTypeExpressionForNamespace(activity.OutputTypeSymbol, targetNamespace);
            string inputParameter = inputType + " input";
            if (inputType.EndsWith("?", StringComparison.Ordinal))
            {
                inputParameter += " = default";
            }

            string simplifiedActivityTypeName = SimplifyTypeNameForNamespace(activity.TypeName, targetNamespace);

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
            string simplifiedEntityTypeName = SimplifyTypeNameForNamespace(entity.TypeName, targetNamespace);

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
                DurableTaskKind kind)
            {
                this.TypeName = taskType;
                this.Namespace = taskNamespace;
                this.TaskName = taskName;
                this.Kind = kind;

                // Entities only have a state type parameter, not input/output
                if (kind == DurableTaskKind.Entity)
                {
                    this.InputType = string.Empty;
                    this.InputParameter = string.Empty;
                    this.OutputType = string.Empty;
                    this.InputTypeSymbol = null;
                    this.OutputTypeSymbol = null;
                }
                else
                {
                    this.InputTypeSymbol = inputType;
                    this.OutputTypeSymbol = outputType;
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
            public string Namespace { get; }
            public string TaskName { get; }
            public string InputType { get; }
            public string InputParameter { get; }
            public string OutputType { get; }
            public DurableTaskKind Kind { get; }
            public ITypeSymbol? InputTypeSymbol { get; }
            public ITypeSymbol? OutputTypeSymbol { get; }

            public bool IsActivity => this.Kind == DurableTaskKind.Activity;

            public bool IsOrchestrator => this.Kind == DurableTaskKind.Orchestrator;

            public bool IsEntity => this.Kind == DurableTaskKind.Entity;

            /// <summary>
            /// Gets a rendered type expression for the given type symbol relative to a target namespace.
            /// </summary>
            public static string GetRenderedTypeExpressionForNamespace(ITypeSymbol? symbol, string targetNamespace)
            {
                if (symbol == null)
                {
                    return "object";
                }

                string expression = symbol.ToDisplayString();
                
                // Simplify System types
                if (expression.StartsWith("System.", StringComparison.Ordinal)
                    && symbol.ContainingNamespace.Name == "System")
                {
                    expression = expression.Substring("System.".Length);
                }
                // Simplify types in the same namespace
                else if (symbol.ContainingNamespace.ToDisplayString() == targetNamespace)
                {
                    // Use the simple name if the type is in the same namespace
                    expression = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                }

                return expression;
            }

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
