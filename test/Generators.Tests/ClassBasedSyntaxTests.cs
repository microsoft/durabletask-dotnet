// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Generators.Tests.Utils;

namespace Microsoft.DurableTask.Generators.Tests;

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
/// <summary>
/// Schedules a new instance of the <see cref=""MyOrchestrator""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, {input}, StartOrchestrationOptions? options = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}}

/// <summary>
/// Calls the <see cref=""MyOrchestrator""/> sub-orchestrator.
/// </summary>
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
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
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
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
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
/// <summary>
/// Calls the <see cref=""MyNS.MyActivityImpl""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
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
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    public Task Entities_PrimitiveStateTypes(string type)
    {
        string code = $@"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

[DurableTask(nameof(MyEntity))]
class MyEntity : TaskEntity<{type}>
{{
    public {type} Get() => this.State;
}}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddEntity<MyEntity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Entities_CustomStateTypes()
    {
        string code = @"
using System.Threading.Tasks;
using MyNS;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

[DurableTask(nameof(MyEntity))]
class MyEntity : TaskEntity<MyClass>
{
    public MyClass Get() => this.State;
}

namespace MyNS
{
    public class MyClass { }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            generatedClassName: "GeneratedDurableTaskExtensions",
            methodList: @"
internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddEntity<MyEntity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Entities_ExplicitNaming()
    {
        // The [DurableTask] attribute is expected to override the entity class name
        string code = @"
using System.Threading.Tasks;
using MyNS;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace MyNS
{
    [DurableTask(""MyEntity"")]
    class MyEntityImpl : TaskEntity<MyClass>
    {
        public MyClass Get() => this.State;
    }

    public class MyClass { }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddEntity<MyNS.MyEntityImpl>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Entities_Inheritance()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

[DurableTask(nameof(MyEntity))]
class MyEntity : MyEntityBase
{
    public override int Get() => this.State;
}

abstract class MyEntityBase : TaskEntity<int>
{
    public abstract int Get();
}";

        // NOTE: Same output as Entities_PrimitiveStateTypes
        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddEntity<MyEntity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Mixed_OrchestratorActivityEntity()
    {
        // Test that the generator handles a mix of orchestrators, activities and entities
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(string.Empty);
}

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(nameof(MyEntity))]
class MyEntity : TaskEntity<int>
{
    public int Get() => this.State;
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
    builder.AddOrchestrator<MyOrchestrator>();
    builder.AddActivity<MyActivity>();
    builder.AddEntity<MyEntity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Events_BasicRecord()
    {
        string code = @"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableEvent(nameof(ApprovalEvent))]
public sealed record ApprovalEvent(bool Approved, string? Approver);";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Waits for an external event of type <see cref=""ApprovalEvent""/>.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)""/>
public static Task<ApprovalEvent> WaitForApprovalEventAsync(this TaskOrchestrationContext context, CancellationToken cancellationToken = default)
{
    return context.WaitForExternalEvent<ApprovalEvent>(""ApprovalEvent"", cancellationToken);
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Events_ClassWithExplicitName()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableEvent(""CustomEventName"")]
public class MyEventData
{
    public string Message { get; set; }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Waits for an external event of type <see cref=""MyEventData""/>.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)""/>
public static Task<MyEventData> WaitForCustomEventNameAsync(this TaskOrchestrationContext context, CancellationToken cancellationToken = default)
{
    return context.WaitForExternalEvent<MyEventData>(""CustomEventName"", cancellationToken);
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Events_WithNamespace()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;
using MyNS;

namespace MyNS
{
    [DurableEvent(nameof(DataReceivedEvent))]
    public record DataReceivedEvent(int Id, string Data);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Waits for an external event of type <see cref=""MyNS.DataReceivedEvent""/>.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)""/>
public static Task<MyNS.DataReceivedEvent> WaitForDataReceivedEventAsync(this TaskOrchestrationContext context, CancellationToken cancellationToken = default)
{
    return context.WaitForExternalEvent<MyNS.DataReceivedEvent>(""DataReceivedEvent"", cancellationToken);
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }
}
