// ----------------------------------------------------------------------------------
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System.Threading.Tasks;
using DurableTask.Generators.Tests.Utils;
using Xunit;

namespace DurableTask.Generators.Tests;

public class ClassBasedSyntaxTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public Task Orchestrators_PrimitiveTypes()
    {
        string code = @"
using System.Threading.Tasks;
using DurableTask;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestratorBase<int, string>
{
    protected override Task<string> OnRunAsync(int input) => Task.FromResult(string.Empty);
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

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
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
using DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivityBase<int, string>
{
    protected override string OnRun(int input) => default;
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
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
using DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivityBase<MyClass, MyClass>
{
    protected override MyClass OnRun(MyClass input) => default;
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

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
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
using DurableTask;

namespace MyNS
{
    [DurableTask(""MyActivity"")]
    class MyActivityImpl : TaskActivityBase<MyClass, MyClass>
    {
        protected override MyClass OnRun(MyClass input) => default;
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

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
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
}
