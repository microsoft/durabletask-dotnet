// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Dapr.DurableTask.Analyzers.Orchestration;
using VerifyCS = Dapr.DurableTask.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<Dapr.DurableTask.Analyzers.Orchestration.DelayOrchestrationAnalyzer, Dapr.DurableTask.Analyzers.Orchestration.DelayOrchestrationFixer>;

namespace Dapr.DurableTask.Analyzers.Tests.Orchestration;

public class DelayOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeWithSymbolsAvailableHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingThreadSleepHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    {|#0:Thread.Sleep(1000)|};
}
");
        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "Thread.Sleep(int)", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingTaskDelayHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
async Task Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    CancellationToken t = CancellationToken.None;
    await {|#0:Task.Delay(1000, t)|};
}
");

        string fix = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
async Task Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    CancellationToken t = CancellationToken.None;
    await context.CreateTimer(TimeSpan.FromMilliseconds(1000), t);
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "Task.Delay(int, CancellationToken)", "Run");

        await VerifyCS.VerifyDurableTaskCodeFixAsync(code, expected, fix);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingTaskTDelayHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
async Task Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    await {|#0:Task<object>.Delay(1000)|};
}
");
        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "Task.Delay(int)", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskOrchestratorUsingThreadSleepHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        Method();
        return Task.FromResult(input);
    }

    private void Method() {
        {|#0:Thread.Sleep(1000)|};
    }
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "Thread.Sleep(int)", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task FuncOrchestratorUsingThreadSleepHasDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", context =>
{
    {|#0:Thread.Sleep(1000)|};
});
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Main", "Thread.Sleep(int)", "HelloSequence");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(DelayOrchestrationAnalyzer.DiagnosticId);
    }
}
