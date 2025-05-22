// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Generators.Tests.Utils;

namespace Dapr.DurableTask.Generators.Tests;

public class ClassBasedSyntaxTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Theory]
    [InlineData("int", "int input")]
    [InlineData("int?", "int? input = default")]
    [InlineData("string", "string input")]
    [InlineData("string?", "string? input = default")]
    public Task Orchestrators_PrimitiveTypes(string type, string input)
    {
        string code = $@"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestrator<{type}, string>
{{
    public override Task<string> RunAsync(TaskOrchestrationContext ctx, {type} input) => Task.FromResult(string.Empty);
}}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, {input}, StartOrchestrationOptions? options = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}}

/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context, {input}, TaskOptions? options = null)
{{
    return context.CallSubOrchestratorAsync<string>(""MyOrchestrator"", input, options);
}}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{{
    builder.AddOrchestrator<MyOrchestrator>();
    return builder;
}}");

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
    public override Task<string> RunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(this.X);
}

abstract class MyOrchestratorBase : TaskOrchestrator<int, string>
{
    public virtual string X => ""Foo"";
}";

        // NOTE: Same output as Orchestrators_PrimitiveTypes
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}

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
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);

    }

    [Theory]
    [InlineData("int", "int input")]
    [InlineData("int?", "int? input = default")]
    [InlineData("string", "string input")]
    [InlineData("string?", "string? input = default")]
    public Task Activities_PrimitiveTypes(string type, string input)
    {
        string code = $@"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<{type}, string>
{{
    public override Task<string> RunAsync(TaskActivityContext context, {type} input) => Task.FromResult(string.Empty);
}}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, {input}, TaskOptions? options = null)
{{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{{
    builder.AddActivity<MyActivity>();
    return builder;
}}");

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
using System.Threading.Tasks;
using MyNS;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<MyClass, MyClass>
{
    public override Task<MyClass> RunAsync(TaskActivityContext context, MyClass input) => Task.FromResult(input);
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
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
using System.Threading.Tasks;
using MyNS;
using Microsoft.DurableTask;

namespace MyNS
{
    [DurableTask(""MyActivity"")]
    class MyActivityImpl : TaskActivity<MyClass, MyClass>
    {
        public override Task<MyClass> RunAsync(TaskActivityContext context, MyClass input) => Task.FromResult(input);
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
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
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : MyActivityBase
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}

abstract class MyActivityBase : TaskActivity<int, string>
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
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
