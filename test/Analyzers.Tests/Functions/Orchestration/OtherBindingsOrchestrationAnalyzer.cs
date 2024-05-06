// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Functions.Orchestration;
using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Functions.Orchestration.OtherBindingsOrchestrationAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Functions.Orchestration;

public class OtherBindingsOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationWithNoBannedBindingHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingDurableClientHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context, {|#0:[DurableClient] DurableTaskClient client|})
{
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingEntityTriggerHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context, {|#0:[EntityTrigger] TaskEntityDispatcher dispatcher|})
{
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }


    [Fact]
    public async Task DurableFunctionOrchestrationUsingMultipleBannedBindingsHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context,
{|#0:[EntityTrigger] TaskEntityDispatcher dispatcher|},
{|#1:[DurableClient] DurableTaskClient client|})
{
}
");

        DiagnosticResult[] expected = Enumerable.Range(0, 2).Select(
            i => BuildDiagnostic().WithLocation(i).WithArguments("Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(OtherBindingsOrchestrationAnalyzer.DiagnosticId);
    }
}
