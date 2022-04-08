﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.DurableTask.Generators.Tests.Utils;
using Xunit;

namespace Microsoft.DurableTask.Generators.Tests;

public class ClassBasedSyntaxTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public Task Orchestrators_PrimitiveTypes()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestratorBase<int, string>
{
    protected override Task<string> OnRunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <inheritdoc cref=""DurableTaskClient.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this DurableTaskClient client,
    string? instanceId = null,
    int input = default,
    DateTimeOffset? startTime = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(
        ""MyOrchestrator"",
        instanceId,
        input,
        startTime);
}

/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context,
    string? instanceId = null,
    int input = default,
    TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(
        ""MyOrchestrator"",
        instanceId,
        input,
        options);
}

public static IDurableTaskRegistry AddAllGeneratedTasks(this IDurableTaskRegistry builder)
{
    builder.AddOrchestrator<MyOrchestrator>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Orchestrators_Inheritance()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyOrchestrator))]
sealed class MyOrchestrator : MyOrchestratorBase
{
    protected override Task<string> OnRunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(this.X);
}

abstract class MyOrchestratorBase : TaskOrchestratorBase<int, string>
{
    public virtual string X => ""Foo"";
}";

        // NOTE: Same output as Orchestrators_PrimitiveTypes
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <inheritdoc cref=""DurableTaskClient.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this DurableTaskClient client,
    string? instanceId = null,
    int input = default,
    DateTimeOffset? startTime = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(
        ""MyOrchestrator"",
        instanceId,
        input,
        startTime);
}

/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context,
    string? instanceId = null,
    int input = default,
    TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(
        ""MyOrchestrator"",
        instanceId,
        input,
        options);
}

public static IDurableTaskRegistry AddAllGeneratedTasks(this IDurableTaskRegistry builder)
{
    builder.AddOrchestrator<MyOrchestrator>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);

    }

    [Fact]
    public Task Activities_PrimitiveTypes()
    {
        string code = @"
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivityBase<int, string>
{
    protected override string OnRun(TaskActivityContext context, int input) => default;
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

public static IDurableTaskRegistry AddAllGeneratedTasks(this IDurableTaskRegistry builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Activities_CustomTypes()
    {
        string code = @"
using MyNS;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivityBase<MyClass, MyClass>
{
    protected override MyClass OnRun(TaskActivityContext context, MyClass input) => default;
}

namespace MyNS
{
    public class MyClass { }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            generatedClassName: "GeneratedDurableTaskExtensions",
            methodList: @"
public static Task<MyNS.MyClass> CallMyActivityAsync(this TaskOrchestrationContext ctx, MyNS.MyClass input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<MyNS.MyClass>(""MyActivity"", input, options);
}

public static IDurableTaskRegistry AddAllGeneratedTasks(this IDurableTaskRegistry builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }


    [Fact]
    public Task Activities_ExplicitNaming()
    {
        // The [DurableTask] attribute is expected to override the activity class name
        string code = @"
using MyNS;
using Microsoft.DurableTask;

namespace MyNS
{
    [DurableTask(""MyActivity"")]
    class MyActivityImpl : TaskActivityBase<MyClass, MyClass>
    {
        protected override MyClass OnRun(TaskActivityContext context, MyClass input) => default;
    }

    public class MyClass { }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<MyNS.MyClass> CallMyActivityAsync(this TaskOrchestrationContext ctx, MyNS.MyClass input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<MyNS.MyClass>(""MyActivity"", input, options);
}

public static IDurableTaskRegistry AddAllGeneratedTasks(this IDurableTaskRegistry builder)
{
    builder.AddActivity<MyNS.MyActivityImpl>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Activities_Inheritance()
    {
        string code = @"
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : MyActivityBase
{
    protected override string OnRun(TaskActivityContext context, int input) => default;
}

abstract class MyActivityBase : TaskActivityBase<int, string>
{
}";

        // NOTE: Same output as Activities_PrimitiveTypes
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

public static IDurableTaskRegistry AddAllGeneratedTasks(this IDurableTaskRegistry builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }
}
