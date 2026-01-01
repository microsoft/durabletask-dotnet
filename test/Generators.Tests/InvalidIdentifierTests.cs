// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Generators.Tests.Utils;

namespace Microsoft.DurableTask.Generators.Tests;

public class InvalidIdentifierTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Theory]
    [InlineData("Foo.Bar")]
    [InlineData("Foo-Bar")]
    [InlineData("Foo Bar")]
    [InlineData("123Invalid")]
    [InlineData("My-Task")]
    [InlineData("Task.Name")]
    [InlineData("@InvalidName")]
    [InlineData("Task#Name")]
    public Task Activity_InvalidName_ReportsDiagnostic(string invalidName)
    {
        string code = $@"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""{invalidName}"")]
class MyActivity : TaskActivity<int, string>
{{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}}";

        // The test framework automatically verifies that the expected diagnostic is reported
        DiagnosticResult expected = new DiagnosticResult("DURABLE3001", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 5, 14, 5, 14 + invalidName.Length + 2)
            .WithArguments(invalidName);

        CSharpSourceGeneratorVerifier<DurableTaskSourceGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { code },
                ExpectedDiagnostics = { expected },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                },
            },
        };

        return test.RunAsync();
    }

    [Theory]
    [InlineData("Foo.Bar")]
    [InlineData("Foo-Bar")]
    [InlineData("Foo Bar")]
    [InlineData("123Invalid")]
    public Task Orchestrator_InvalidName_ReportsDiagnostic(string invalidName)
    {
        string code = $@"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""{invalidName}"")]
class MyOrchestrator : TaskOrchestrator<int, string>
{{
    public override Task<string> RunAsync(TaskOrchestrationContext ctx, int input) => Task.FromResult(string.Empty);
}}";

        DiagnosticResult expected = new DiagnosticResult("DURABLE3001", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 5, 14, 5, 14 + invalidName.Length + 2)
            .WithArguments(invalidName);

        CSharpSourceGeneratorVerifier<DurableTaskSourceGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { code },
                ExpectedDiagnostics = { expected },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                },
            },
        };

        return test.RunAsync();
    }

    [Theory]
    [InlineData("Foo.Bar")]
    [InlineData("Foo-Bar")]
    [InlineData("Event Name")]
    public Task Event_InvalidName_ReportsDiagnostic(string invalidName)
    {
        string code = $@"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableEvent(""{invalidName}"")]
public sealed record MyEvent(bool Approved);";

        DiagnosticResult expected = new DiagnosticResult("DURABLE3002", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 5, 15, 5, 15 + invalidName.Length + 2)
            .WithArguments(invalidName);

        CSharpSourceGeneratorVerifier<DurableTaskSourceGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { code },
                ExpectedDiagnostics = { expected },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                },
            },
        };

        return test.RunAsync();
    }

    [Fact]
    public Task Activity_ValidName_NoDiagnostic()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""MyActivity"")]
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
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Activity_ValidNameWithUnderscore_NoDiagnostic()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""My_Activity"")]
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
public static Task<string> CallMy_ActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""My_Activity"", input, options);
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
    public Task Activity_InvalidName_NoCodeGenerated()
    {
        // When a task has an invalid name, we should report a diagnostic
        // but NOT generate any code for it (to avoid compilation errors)
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""Foo.Bar"")]
class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        DiagnosticResult expected = new DiagnosticResult("DURABLE3001", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 5, 14, 5, 23)
            .WithArguments("Foo.Bar");

        CSharpSourceGeneratorVerifier<DurableTaskSourceGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { code },
                ExpectedDiagnostics = { expected },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                },
                // Don't expect any generated sources since the name is invalid
            },
        };

        return test.RunAsync();
    }
}
