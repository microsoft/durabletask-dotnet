// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Orchestration.ThreadTaskOrchestrationAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class ThreadTaskOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task StartingThreadsTasksAreBannedWithinAzureFunctionOrchestrations()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
async Task Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    {|#0:new Thread(() => { }).Start()|};

    Task<int> t1 = {|#1:Task<int>.Run(() => 0)|};
    await {|#2:t1.ContinueWith(task => 0)|};
    await {|#3:Task<int>.Factory.StartNew(() => 0)|};

    Task t2 = {|#4:Task.Run(() => { })|};
    await {|#5:t2.ContinueWith(task => { })|};
    await {|#6:Task.Factory.StartNew(() => { })|};
}
");
        string[] invocations = [
            "Thread.Start()",
            "Task.Run<int>(Func<int>)",
            "Task<int>.ContinueWith<int>(Func<Task<int>, int>)",
            "TaskFactory<int>.StartNew(Func<int>)",
            "Task.Run(Action)",
            "Task.ContinueWith(Action<Task>)",
            "TaskFactory.StartNew(Action)",
        ];

        DiagnosticResult[] expected = invocations.Select(
            (invocation, i) => BuildDiagnostic().WithLocation(i).WithArguments("Method", invocation, "Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(ThreadTaskOrchestrationAnalyzer.DiagnosticId);
    }
}
