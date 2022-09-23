// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Generators.Tests.Utils;
using Xunit;

namespace Microsoft.DurableTask.Generators.Tests;

public class AzureFunctionsTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public async Task Activities_SimpleFunctionTrigger()
    {
        string code = @"
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

public class Calculator
{
    [Function(nameof(Identity))]
    public int Identity([ActivityTrigger] int input) => input;
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<int> CallIdentityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<int>(""Identity"", input, options);
}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }

    [Fact]
    public async Task Activities_SimpleFunctionTrigger_TaskReturning()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

public class Calculator
{
    [Function(""Identity"")]
    public Task<int> IdentityAsync([ActivityTrigger] int input) => Task.FromResult(input);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<int> CallIdentityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<int>(""Identity"", input, options);
}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }

    [Fact]
    public async Task Activities_SimpleFunctionTrigger_CustomType()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

namespace AzureFunctionsTests
{
    public record Input(int Value);

    public class Calculator
    {
        [Function(""Identity"")]
        public Task<Input> Identity([ActivityTrigger] Input input) => Task.FromResult(input);
    }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<AzureFunctionsTests.Input> CallIdentityAsync(this TaskOrchestrationContext ctx, AzureFunctionsTests.Input input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<AzureFunctionsTests.Input>(""Identity"", input, options);
}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based activity syntax generates a <see cref="TaskOrchestrationContext"/>
    /// extension method as well as an <see cref="ActivityTriggerAttribute"/> function definition.
    /// </summary>
    /// <param name="inputType">The activity input type.</param>
    /// <param name="outputType">The activity output type.</param>
    [Theory]
    [InlineData("int", "string")]
    [InlineData("string", "int")]
    [InlineData("Guid", "TimeSpan")]
    public async Task Activities_ClassBasedSyntax(string inputType, string outputType)
    {
        // Nullable reference types need to be used for input expressions
        string defaultInputType = TestHelpers.GetDefaultInputType(inputType);

        string code = $@"
using System;
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
public class MyActivity : TaskActivityBase<{inputType}, {outputType}>
{{
    protected override {outputType} OnRun(TaskActivityContext context, {inputType} input) => default!;
}}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
public static Task<{outputType}> CallMyActivityAsync(this TaskOrchestrationContext ctx, {inputType} input, TaskOptions? options = null)
{{
    return ctx.CallActivityAsync<{outputType}>(""MyActivity"", input, options);
}}

[Function(nameof(MyActivity))]
public static async Task<{outputType}> MyActivity([ActivityTrigger] {defaultInputType} input, string instanceId, FunctionContext executionContext)
{{
    ITaskActivity activity = ActivatorUtilities.CreateInstance<MyActivity>(executionContext.InstanceServices);
    TaskActivityContext context = new GeneratedActivityContext(""MyActivity"", instanceId);
    object? result = await activity.RunAsync(context, input);
    return ({outputType})result!;
}}
{TestHelpers.DeIndent(DurableTaskSourceGenerator.GetGeneratedActivityContextCode(), spacesToRemove: 8)}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based syntax for authoring orchestrations generates 
    /// type-safe <see cref="DurableTaskClient"/> and <see cref="TaskOrchestrationContext"/> 
    /// extension methods as well as <see cref="OrchestrationTriggerAttribute"/> function triggers.
    /// </summary>
    /// <param name="inputType">The activity input type.</param>
    /// <param name="outputType">The activity output type.</param>
    [Theory]
    [InlineData("int", "string?")]
    [InlineData("string", "int")]
    [InlineData("(int, int)", "(double, double)")]
    [InlineData("DateTime?", "DateTimeOffset?")]
    public async Task Orchestrators_ClassBasedSyntax(string inputType, string outputType)
    {
        // Nullable reference types need to be used for input expressions
        string defaultInputType = TestHelpers.GetDefaultInputType(inputType);

        string code = $@"
#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.DurableTask;

namespace MyNS
{{
    [DurableTask(nameof(MyOrchestrator))]
    public class MyOrchestrator : TaskOrchestratorBase<{inputType}, {outputType}>
    {{
        protected override Task<{outputType}> OnRunAsync(TaskOrchestrationContext ctx, {defaultInputType} input) => throw new NotImplementedException();
    }}
}}";
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
static readonly ITaskOrchestrator singletonMyOrchestrator = new MyNS.MyOrchestrator();

[Function(nameof(MyOrchestrator))]
public static Task<{outputType}> MyOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
{{
    return singletonMyOrchestrator.RunAsync(context, context.GetInput<{inputType}>())
        .ContinueWith(t => ({outputType})(t.Result ?? default({outputType})!), TaskContinuationOptions.ExecuteSynchronously);
}}

/// <inheritdoc cref=""DurableTaskClient.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this DurableTaskClient client,
    string? instanceId = null,
    {defaultInputType} input = default,
    DateTimeOffset? startTime = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(
        ""MyOrchestrator"",
        instanceId,
        input,
        startTime);
}}

/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
public static Task<{outputType}> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context,
    string? instanceId = null,
    {defaultInputType} input = default,
    TaskOptions? options = null)
{{
    return context.CallSubOrchestratorAsync<{outputType}>(
        ""MyOrchestrator"",
        instanceId,
        input,
        options);
}}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based syntax for authoring orchestrations generates 
    /// type-safe <see cref="DurableTaskClient"/> and <see cref="TaskOrchestrationContext"/> 
    /// extension methods as well as <see cref="OrchestrationTriggerAttribute"/> function triggers.
    /// </summary>
    /// <param name="inputType">The activity input type.</param>
    /// <param name="outputType">The activity output type.</param>
    [Theory]
    [InlineData("int", "string?")]
    [InlineData("string", "int")]
    [InlineData("(int, int)", "(double, double)")]
    [InlineData("DateTime?", "DateTimeOffset?")]
    public async Task Orchestrators_ClassBasedSyntax_Inheritance(string inputType, string outputType)
    {
        // Nullable reference types need to be used for input expressions
        string defaultInputType = TestHelpers.GetDefaultInputType(inputType);

        string code = $@"
#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.DurableTask;

namespace MyNS
{{
    [DurableTask]
    public class MyOrchestrator : MyOrchestratorBase
    {{
        protected override Task<{outputType}> OnRunAsync(TaskOrchestrationContext ctx, {defaultInputType} input) => throw new NotImplementedException();
    }}

    public abstract class MyOrchestratorBase : TaskOrchestratorBase<{inputType}, {outputType}>
    {{
    }}
}}";
        // Same output as Orchestrators_ClassBasedSyntax
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
static readonly ITaskOrchestrator singletonMyOrchestrator = new MyNS.MyOrchestrator();

[Function(nameof(MyOrchestrator))]
public static Task<{outputType}> MyOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
{{
    return singletonMyOrchestrator.RunAsync(context, context.GetInput<{inputType}>())
        .ContinueWith(t => ({outputType})(t.Result ?? default({outputType})!), TaskContinuationOptions.ExecuteSynchronously);
}}

/// <inheritdoc cref=""DurableTaskClient.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this DurableTaskClient client,
    string? instanceId = null,
    {defaultInputType} input = default,
    DateTimeOffset? startTime = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(
        ""MyOrchestrator"",
        instanceId,
        input,
        startTime);
}}

/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
public static Task<{outputType}> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context,
    string? instanceId = null,
    {defaultInputType} input = default,
    TaskOptions? options = null)
{{
    return context.CallSubOrchestratorAsync<{outputType}>(
        ""MyOrchestrator"",
        instanceId,
        input,
        options);
}}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }
}
