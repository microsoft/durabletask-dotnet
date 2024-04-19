// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Orchestration.DateTimeOrchestrationAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class DateTimeOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeWithNoSymbolsAvailableHasNoDiag()
    {
        string code = @"";

        // checks that empty code with no assembly references of Durable Functions has no diagnostics.
        // this guarantees that if someone adds our analyzer to a project that doesn't use Durable Functions,
        // the analyzer won't crash/they won't get any diagnostics
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Fact]
    public async Task EmptyCodeWithSymbolsAvailableHasNoDiag()
    {
        string code = @"";

        // checks that empty code with access to assembly references of Durable Functions has no diagnostics
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task NonOrchestrationHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
void Method(){
    Console.WriteLine(DateTime.Now);
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Theory]
    [InlineData("DateTime.Now")]
    [InlineData("DateTime.UtcNow")]
    [InlineData("DateTime.Today")]
    public async Task DurableFunctionOrchestrationUsingDateTimeNonDeterministicPropertiesHasDiag(string expression)
    {
        string code = Wrapper.WrapDurableFunctionOrchestration($@"
[Function(""Run"")]
DateTime Run([OrchestrationTrigger] TaskOrchestrationContext context)
{{
    return {{|#0:{expression}|}};
}}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run", $"System.{expression}", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationInvokingChainedMethodsHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run1"")]
long Run1([OrchestrationTrigger] TaskOrchestrationContext context)  => Level1();

[Function(""Run2"")]
long Run2([OrchestrationTrigger] TaskOrchestrationContext context)  => Level1();

long Level1() => Level2();

long Level2() => Level3();

long Level3() => {|#0:DateTime.Now|}.Ticks;
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Level3", "System.DateTime.Now", "Run1, Run2");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationInvokingRecursiveMethodsHasSingleDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
long Run([OrchestrationTrigger] TaskOrchestrationContext context) => RecursiveMethod(0);

long RecursiveMethod(int i){
    if (i == 10) return 1;
    DateTime date = {|#0:DateTime.Now|};
    return date.Ticks + RecursiveMethod(i + 1);
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("RecursiveMethod", "System.DateTime.Now", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationInvokingMethodMultipleTimesHasSingleDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    _ = Method();
    _ = Method();
    _ = Method();
}

DateTime Method() => {|#0:DateTime.Now|};
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "System.DateTime.Now", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationInvokingAsyncMethodsHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(nameof(Run))]
async Task Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    _ = await ValueTaskInvocation();
    _ = await TaskInvocation();
}

static ValueTask<DateTime> ValueTaskInvocation() => ValueTask.FromResult({|#0:DateTime.Now|});

static Task<DateTime> TaskInvocation() => Task.FromResult({|#1:DateTime.Now|});
");

        DiagnosticResult valueTaskExpected = BuildDiagnostic().WithLocation(0).WithArguments("ValueTaskInvocation", "System.DateTime.Now", "Run");
        DiagnosticResult taskExpected = BuildDiagnostic().WithLocation(1).WithArguments("TaskInvocation", "System.DateTime.Now", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, valueTaskExpected, taskExpected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingLambdasHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    static DateTime fn0() => {|#0:DateTime.Now|};
    Func<DateTime> fn1 = () => {|#1:DateTime.Now|};
    Func<int, DateTime> fn2 = days => {|#2:DateTime.Now|}.AddDays(days);
    Action<int> fn3 = days => Console.WriteLine({|#3:DateTime.Now|}.AddDays(days));
}
");

        DiagnosticResult[] expected = Enumerable.Range(0, 4).Select(
            i => BuildDiagnostic().WithLocation(i).WithArguments($"Run", "System.DateTime.Now", "Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationNotInvokingMethodHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
DateTime Run([OrchestrationTrigger] TaskOrchestrationContext context) => new DateTime(2024, 1, 1);

DateTime NotCalled() => DateTime.Now;
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }


    [Fact]
    public async Task TaskOrchestratorHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, DateTime>
{
    public override Task<DateTime> RunAsync(TaskOrchestrationContext context, string input)
    {
        return Task.FromResult(Method());
    }

    private DateTime Method() => {|#0:DateTime.Now|};
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "System.DateTime.Now", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskOrchestratorImplementingInterfaceHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : ITaskOrchestrator
{
    public Type InputType => typeof(object);
    public Type OutputType => typeof(object);

    public Task<object?> RunAsync(TaskOrchestrationContext context, object? input)
    {
        return Task.FromResult((object?)Method());
    }

    private DateTime Method() => {|#0:DateTime.Now|};
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "System.DateTime.Now", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }


    [Fact]
    public async Task FuncOrchestratorWithLambdaHasDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", context =>
{
    return {|#0:DateTime.Now|};
});
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Main", "System.DateTime.Now", "HelloSequence");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task FuncOrchestratorWithMethodReferenceHasDiag()
    {
        string code = @"
using System;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public static void Main()
    {
        new ServiceCollection().AddDurableTaskWorker(builder =>
        {
            builder.AddTasks(tasks =>
            {
                tasks.AddOrchestratorFunc(""MyRun"", MyRunAsync);
            });
        });
    }

    static DateTime MyRunAsync(TaskOrchestrationContext context)
    {
        return {|#0:DateTime.Now|};
    }
}
";

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("MyRunAsync", "System.DateTime.Now", "MyRun");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }


    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(DateTimeOrchestrationAnalyzer.DiagnosticId);
    }
}
