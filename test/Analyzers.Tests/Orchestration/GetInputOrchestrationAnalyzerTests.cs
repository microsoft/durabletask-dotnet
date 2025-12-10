// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpCodeFixVerifier<Microsoft.DurableTask.Analyzers.Orchestration.GetInputOrchestrationAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class GetInputOrchestrationAnalyzerTests
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
    // This is not an orchestration method, so no diagnostic
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingGetInputHasInfoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
int Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    int input = {|#0:context.GetInput<int>()|};
    return input;
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationWithInputParameterHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
int Run([OrchestrationTrigger] TaskOrchestrationContext context, int input)
{
    // Using input parameter is the recommended approach
    return input;
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorWithInputParameterHasNoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<int, int>
{
    public override Task<int> RunAsync(TaskOrchestrationContext context, int input)
    {
        // Using input parameter is the recommended approach
        return Task.FromResult(input);
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorUsingGetInputHasInfoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<int, int>
{
    public override Task<int> RunAsync(TaskOrchestrationContext context, int input)
    {
        // Even though input parameter exists, GetInput is still flagged as not recommended
        int value = {|#0:context.GetInput<int>()|};
        return Task.FromResult(value);
    }
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task OrchestratorFuncUsingGetInputHasInfoDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""MyOrchestration"", (TaskOrchestrationContext context) =>
{
    int input = {|#0:context.GetInput<int>()|};
    return Task.FromResult(input);
});
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("MyOrchestration");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task NestedMethodCallWithGetInputHasInfoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
int Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    return HelperMethod(context);
}

int HelperMethod(TaskOrchestrationContext context)
{
    int input = {|#0:context.GetInput<int>()|};
    return input;
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task MultipleGetInputCallsHaveMultipleDiags()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
int Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    int input1 = {|#0:context.GetInput<int>()|};
    int input2 = {|#1:context.GetInput<int>()|};
    return input1 + input2;
}
");

        DiagnosticResult expected1 = BuildDiagnostic().WithLocation(0).WithArguments("Run");
        DiagnosticResult expected2 = BuildDiagnostic().WithLocation(1).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected1, expected2);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(GetInputOrchestrationAnalyzer.DiagnosticId);
    }
}
