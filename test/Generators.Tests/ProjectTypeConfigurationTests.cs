// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Generators.Tests.Utils;

namespace Microsoft.DurableTask.Generators.Tests;

public class ProjectTypeConfigurationTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public Task ExplicitWorkerMode_WithFunctionsReference_GeneratesWorkerCode()
    {
        // Test that explicit "Worker" configuration overrides the Functions reference
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        // Even though we have Functions references, we should get Worker code (AddAllGeneratedTasks)
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}",
            isDurableFunctions: false);

        // Pass isDurableFunctions: true to add Functions references, but projectType: "Worker" to override
        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "Worker");
    }

    [Fact]
    public Task ExplicitDurableTaskSchedulerMode_WithFunctionsReference_GeneratesWorkerCode()
    {
        // Test that explicit "DurableTaskScheduler" configuration overrides the Functions reference
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""MyOrchestrator""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}

/// <summary>
/// Calls the <see cref=""MyOrchestrator""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""MyOrchestrator"", input, options);
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<MyOrchestrator>();
    return builder;
}",
            isDurableFunctions: false);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "DurableTaskScheduler");
    }

    [Fact]
    public Task ExplicitFunctionsMode_WithoutFunctionsReference_GeneratesFunctionsCode()
    {
        // Test that explicit "DurableFunctions" configuration generates Functions code
        // even without Functions references
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        // With explicit "DurableFunctions", we should get Functions code (Activity trigger function)
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

[Function(nameof(MyActivity))]
public static async Task<string> MyActivity([ActivityTrigger] int input, string instanceId, FunctionContext executionContext)
{
    ITaskActivity activity = ActivatorUtilities.GetServiceOrCreateInstance<MyActivity>(executionContext.InstanceServices);
    TaskActivityContext context = new GeneratedActivityContext(""MyActivity"", instanceId);
    object? result = await activity.RunAsync(context, input);
    return (string)result!;
}

sealed class GeneratedActivityContext : TaskActivityContext
{
    public GeneratedActivityContext(TaskName name, string instanceId)
    {
        this.Name = name;
        this.InstanceId = instanceId;
    }

    public override TaskName Name { get; }

    public override string InstanceId { get; }
}",
            isDurableFunctions: true);

        // Pass isDurableFunctions: true for expected output, but don't add references
        // Instead rely on projectType: "DurableFunctions" to force Functions mode
        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "DurableFunctions");
    }

    [Fact]
    public Task ExplicitAzureFunctionsMode_WithoutFunctionsReference_GeneratesFunctionsCode()
    {
        // Test that "AzureFunctions" is an alternative spelling
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
static readonly ITaskOrchestrator singletonMyOrchestrator = new MyOrchestrator();

[Function(nameof(MyOrchestrator))]
public static Task<string> MyOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
{
    return singletonMyOrchestrator.RunAsync(context, context.GetInput<int>())
        .ContinueWith(t => (string)(t.Result ?? default(string)!), TaskContinuationOptions.ExecuteSynchronously);
}

/// <summary>
/// Schedules a new instance of the <see cref=""MyOrchestrator""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}

/// <summary>
/// Calls the <see cref=""MyOrchestrator""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""MyOrchestrator"", input, options);
}",
            isDurableFunctions: true);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "AzureFunctions");
    }

    [Fact]
    public Task AutoMode_WithFunctionsReference_GeneratesFunctionsCode()
    {
        // Test that "Auto" mode falls back to auto-detection
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

[Function(nameof(MyActivity))]
public static async Task<string> MyActivity([ActivityTrigger] int input, string instanceId, FunctionContext executionContext)
{
    ITaskActivity activity = ActivatorUtilities.GetServiceOrCreateInstance<MyActivity>(executionContext.InstanceServices);
    TaskActivityContext context = new GeneratedActivityContext(""MyActivity"", instanceId);
    object? result = await activity.RunAsync(context, input);
    return (string)result!;
}

sealed class GeneratedActivityContext : TaskActivityContext
{
    public GeneratedActivityContext(TaskName name, string instanceId)
    {
        this.Name = name;
        this.InstanceId = instanceId;
    }

    public override TaskName Name { get; }

    public override string InstanceId { get; }
}",
            isDurableFunctions: true);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "Auto");
    }

    [Fact]
    public Task AutoMode_WithoutFunctionsReference_GeneratesWorkerCode()
    {
        // Test that "Auto" mode falls back to auto-detection
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}",
            isDurableFunctions: false);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false,
            projectType: "Auto");
    }

    [Fact]
    public Task UnrecognizedMode_WithFunctionsReference_FallsBackToAutoDetection()
    {
        // Test that unrecognized values fall back to auto-detection
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

[Function(nameof(MyActivity))]
public static async Task<string> MyActivity([ActivityTrigger] int input, string instanceId, FunctionContext executionContext)
{
    ITaskActivity activity = ActivatorUtilities.GetServiceOrCreateInstance<MyActivity>(executionContext.InstanceServices);
    TaskActivityContext context = new GeneratedActivityContext(""MyActivity"", instanceId);
    object? result = await activity.RunAsync(context, input);
    return (string)result!;
}

sealed class GeneratedActivityContext : TaskActivityContext
{
    public GeneratedActivityContext(TaskName name, string instanceId)
    {
        this.Name = name;
        this.InstanceId = instanceId;
    }

    public override TaskName Name { get; }

    public override string InstanceId { get; }
}",
            isDurableFunctions: true);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "UnrecognizedValue");
    }

    [Fact]
    public Task DurableTaskWorkerMode_WithFunctionsReference_GeneratesWorkerCode()
    {
        // Test that "DurableTaskWorker" is another valid alternative
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}",
            isDurableFunctions: false);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "DurableTaskWorker");
    }
}
