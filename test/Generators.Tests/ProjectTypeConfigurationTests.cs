// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Generators.Tests.Utils;

namespace Microsoft.DurableTask.Generators.Tests;

public class ProjectTypeConfigurationTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public Task ExplicitStandaloneMode_WithFunctionsReference_GeneratesStandaloneCode()
    {
        // Test that explicit "Standalone" configuration overrides the Functions reference
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        // Even though we have Functions references, we should get Standalone code (AddAllGeneratedTasks)
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

        // Pass isDurableFunctions: true to add Functions references, but projectType: "Standalone" to override
        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "Standalone");
    }

    [Fact]
    public Task ExplicitStandaloneMode_WithFunctionsReference_OrchestratorTest()
    {
        // Test that explicit "Standalone" configuration overrides the Functions reference
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
            projectType: "Standalone");
    }

    [Fact]
    public Task ExplicitFunctionsMode_WithoutFunctionsReference_GeneratesFunctionsCode()
    {
        // Test that explicit "Functions" configuration generates Functions code
        // even without Functions references
        // Note: With Durable Functions v1.11.0+, only extension methods are generated,
        // not [Function] definitions, as the runtime handles class-based tasks natively
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        // With explicit "Functions" and version >= 1.11.0, we only get extension methods
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
}",
            isDurableFunctions: true);

        // Pass isDurableFunctions: true for expected output, but don't add references
        // Instead rely on projectType: "Functions" to force Functions mode
        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "Functions");
    }

    [Fact]
    public Task ExplicitFunctionsMode_OrchestratorTest()
    {
        // Test that "Functions" mode generates orchestrator Functions code
        // Note: With Durable Functions v1.11.0+, only extension methods are generated,
        // not [Function] definitions, as the runtime handles class-based tasks natively
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
}",
            isDurableFunctions: true);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: "Functions");
    }

    [Fact]
    public Task AutoMode_WithFunctionsReference_GeneratesFunctionsCode()
    {
        // Test that "Auto" mode falls back to auto-detection
        // Note: With Durable Functions v1.11.0+, only extension methods are generated,
        // not [Function] definitions, as the runtime handles class-based tasks natively
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
    public Task AutoMode_WithoutFunctionsReference_GeneratesStandaloneCode()
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
        // Note: With Durable Functions v1.11.0+, only extension methods are generated,
        // not [Function] definitions, as the runtime handles class-based tasks natively
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
    public Task NullProjectType_WithoutFunctionsReference_GeneratesStandaloneCode()
    {
        // Test that null projectType (default) falls back to auto-detection
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
            projectType: null);
    }

    [Fact]
    public Task NullProjectType_WithFunctionsReference_GeneratesFunctionsCode()
    {
        // Test that null projectType (default) with Functions reference falls back to auto-detection
        // Note: With Durable Functions v1.11.0+, only extension methods are generated,
        // not [Function] definitions, as the runtime handles class-based tasks natively
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
}",
            isDurableFunctions: true);

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: null);
    }

    [Fact]
    public Task AutoDetect_WithTriggerAttributes_GeneratesFunctionsCode()
    {
        // Test that presence of Azure Functions trigger attributes auto-detects as Functions project
        // This validates the allFunctions.IsDefaultOrEmpty check in DetermineIsDurableFunctions
        string code = @"
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;

public class MyFunctions
{
    [Function(nameof(MyActivity))]
    public int MyActivity([ActivityTrigger] int input) => input;
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""MyFunctions.MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<int> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<int>(""MyActivity"", input, options);
}",
            isDurableFunctions: true);

        // No explicit projectType - should auto-detect based on [ActivityTrigger] attribute
        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true,
            projectType: null);
    }
}
