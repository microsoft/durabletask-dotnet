// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Generators.Tests.Utils;

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
/// <summary>
/// Calls the <see cref=""Calculator.Identity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
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
/// <summary>
/// Calls the <see cref=""Calculator.IdentityAsync""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
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
/// <summary>
/// Calls the <see cref=""AzureFunctionsTests.Calculator.Identity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
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
    /// extension method. With PR #3229, Durable Functions now natively handles class-based invocations,
    /// so the generator no longer creates [Function] attribute definitions to avoid duplicates.
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
public class MyActivity : TaskActivity<{inputType}, {outputType}>
{{
    public override Task<{outputType}> RunAsync(TaskActivityContext context, {inputType} input) => Task.FromResult<{outputType}>(default!);
}}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
/// <summary>
/// Calls the <see cref=""MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<{outputType}> CallMyActivityAsync(this TaskOrchestrationContext ctx, {inputType} input, TaskOptions? options = null)
{{
    return ctx.CallActivityAsync<{outputType}>(""MyActivity"", input, options);
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
    /// extension methods. With PR #3229, Durable Functions now natively handles class-based
    /// invocations, so the generator no longer creates [Function] attribute definitions.
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
    public class MyOrchestrator : TaskOrchestrator<{inputType}, {outputType}>
    {{
        public override Task<{outputType}> RunAsync(TaskOrchestrationContext ctx, {defaultInputType} input) => throw new NotImplementedException();
    }}
}}";
        // Build the expected InputParameter format (matches generator logic)
        string expectedInputParameter = inputType + " input";
        if (inputType.EndsWith('?'))
        {
            expectedInputParameter += " = default";
        }

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
/// <summary>
/// Schedules a new instance of the <see cref=""MyNS.MyOrchestrator""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, {expectedInputParameter}, StartOrchestrationOptions? options = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}}

/// <summary>
/// Calls the <see cref=""MyNS.MyOrchestrator""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<{outputType}> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context, {expectedInputParameter}, TaskOptions? options = null)
{{
    return context.CallSubOrchestratorAsync<{outputType}>(""MyOrchestrator"", input, options);
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
    /// extension methods. With PR #3229, Durable Functions now natively handles class-based
    /// invocations, so the generator no longer creates [Function] attribute definitions.
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
        public override Task<{outputType}> RunAsync(TaskOrchestrationContext ctx, {defaultInputType} input) => throw new NotImplementedException();
    }}

    public abstract class MyOrchestratorBase : TaskOrchestrator<{inputType}, {outputType}>
    {{
    }}
}}";
        // Same output as Orchestrators_ClassBasedSyntax
        // Build the expected InputParameter format (matches generator logic)
        string expectedInputParameter = inputType + " input";
        if (inputType.EndsWith('?'))
        {
            expectedInputParameter += " = default";
        }

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
/// <summary>
/// Schedules a new instance of the <see cref=""MyNS.MyOrchestrator""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, {expectedInputParameter}, StartOrchestrationOptions? options = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}}

/// <summary>
/// Calls the <see cref=""MyNS.MyOrchestrator""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<{outputType}> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context, {expectedInputParameter}, TaskOptions? options = null)
{{
    return context.CallSubOrchestratorAsync<{outputType}>(""MyOrchestrator"", input, options);
}}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based syntax for authoring entities no longer generates
    /// any code for Azure Functions. With PR #3229, Durable Functions now natively handles
    /// class-based invocations. Entities don't have extension methods, so nothing is generated.
    /// </summary>
    /// <param name="stateType">The entity state type.</param>
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    public async Task Entities_ClassBasedSyntax(string stateType)
    {
        string code = $@"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace MyNS
{{
    [DurableTask(nameof(MyEntity))]
    public class MyEntity : TaskEntity<{stateType}>
    {{
        public {stateType} Get() => this.State;
    }}
}}";

        // With PR #3229, no code is generated for class-based entities in Durable Functions
        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutputSource: null, // No output expected
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based syntax for authoring entities with inheritance no longer generates
    /// any code for Azure Functions. With PR #3229, Durable Functions now natively handles class-based invocations.
    /// </summary>
    /// <param name="stateType">The entity state type.</param>
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    public async Task Entities_ClassBasedSyntax_Inheritance(string stateType)
    {
        string code = $@"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace MyNS
{{
    [DurableTask]
    public class MyEntity : MyEntityBase
    {{
        public override {stateType} Get() => this.State;
    }}

    public abstract class MyEntityBase : TaskEntity<{stateType}>
    {{
        public abstract {stateType} Get();
    }}
}}";

        // With PR #3229, no code is generated for class-based entities in Durable Functions
        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutputSource: null, // No output expected
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based syntax for authoring entities with custom state types no longer generates
    /// any code for Azure Functions. With PR #3229, Durable Functions now natively handles class-based invocations.
    /// </summary>
    [Fact]
    public async Task Entities_ClassBasedSyntax_CustomStateType()
    {
        string code = @"
#nullable enable
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace MyNS
{
    public class MyState
    {
        public int Value { get; set; }
    }

    [DurableTask(nameof(MyEntity))]
    public class MyEntity : TaskEntity<MyState>
    {
        public MyState Get() => this.State;
    }
}";

        // With PR #3229, no code is generated for class-based entities in Durable Functions
        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutputSource: null, // No output expected
            isDurableFunctions: true);
    }

    /// <summary>
    /// Verifies that using the class-based syntax for authoring a mix of orchestrators, activities,
    /// and entities generates the appropriate extension methods for Azure Functions.
    /// With PR #3229, Durable Functions now natively handles class-based invocations,
    /// so the generator no longer creates [Function] attribute definitions.
    /// </summary>
    [Fact]
    public async Task Mixed_OrchestratorActivityEntity_ClassBasedSyntax()
    {
        string code = @"
#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace MyNS
{
    [DurableTask(nameof(MyOrchestrator))]
    public class MyOrchestrator : TaskOrchestrator<int, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(string.Empty);
    }

    [DurableTask(nameof(MyActivity))]
    public class MyActivity : TaskActivity<int, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
    }

    [DurableTask(nameof(MyEntity))]
    public class MyEntity : TaskEntity<int>
    {
        public int Get() => this.State;
    }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: $@"
/// <summary>
/// Schedules a new instance of the <see cref=""MyNS.MyOrchestrator""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{{
    return client.ScheduleNewOrchestrationInstanceAsync(""MyOrchestrator"", input, options);
}}

/// <summary>
/// Calls the <see cref=""MyNS.MyOrchestrator""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{{
    return context.CallSubOrchestratorAsync<string>(""MyOrchestrator"", input, options);
}}

/// <summary>
/// Calls the <see cref=""MyNS.MyActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}}",
            isDurableFunctions: true);

        await TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: true);
    }
}
