// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<Microsoft.DurableTask.Analyzers.Orchestration.GuidOrchestrationAnalyzer, Microsoft.DurableTask.Analyzers.Orchestration.GuidOrchestrationFixer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class GuidOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeWithSymbolsAvailableHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingNewGuidHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
Guid Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    return {|#0:Guid.NewGuid()|};
}
");

        string fix = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
Guid Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    return context.NewGuid();
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Method", "Guid.NewGuid()", "Run");

        await VerifyCS.VerifyDurableTaskCodeFixAsync(code, expected, fix);
    }

    [Fact]
    public async Task TaskOrchestratorUsingNewGuidHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, Guid>
{
    public override Task<Guid> RunAsync(TaskOrchestrationContext context, string input)
    {
        return Task.FromResult({|#0:Guid.NewGuid()|});
    }
}
");

        string fix = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, Guid>
{
    public override Task<Guid> RunAsync(TaskOrchestrationContext context, string input)
    {
        return Task.FromResult(context.NewGuid());
    }
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "Guid.NewGuid()", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskCodeFixAsync(code, expected, fix);
    }

    [Fact]
    public async Task FuncOrchestratorUsingNewGuidHasDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", context =>
{
    return {|#0:Guid.NewGuid()|};
});
");

        string fix = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", context =>
{
    return context.NewGuid();
});
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Main", "Guid.NewGuid()", "HelloSequence");

        await VerifyCS.VerifyDurableTaskCodeFixAsync(code, expected, fix);
    }

    [Fact]
    public async Task FuncOrchestratorInForEachWithVariableNameNoDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
string[] names = new[] { ""A"", ""B"" };
foreach (string name in names)
{
    tasks.AddOrchestratorFunc<string, string>(
        name,
        async (ctx, input) =>
        {
            return await ctx.CallActivityAsync<string>(""Activity"", input);
        });
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task FuncOrchestratorInForEachWithVariableNameHasDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
string[] names = new[] { ""A"", ""B"" };
foreach (string name in names)
{
    tasks.AddOrchestratorFunc<string, string>(
        name,
        async (ctx, input) =>
        {
            return {|#0:Guid.NewGuid()|};
        });
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Main", "Guid.NewGuid()", "Main");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }
    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(GuidOrchestrationAnalyzer.DiagnosticId);
    }
}
