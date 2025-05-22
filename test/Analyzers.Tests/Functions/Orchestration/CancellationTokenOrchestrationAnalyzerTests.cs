// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Dapr.DurableTask.Analyzers.Functions.Orchestration;
using VerifyCS = Dapr.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Dapr.DurableTask.Analyzers.Functions.Orchestration.CancellationTokenOrchestrationAnalyzer>;

namespace Dapr.DurableTask.Analyzers.Tests.Functions.Orchestration;

public class CancellationTokenOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingCancellationTokenAsParameterHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context, {|#0:CancellationToken token|})
{
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(CancellationTokenOrchestrationAnalyzer.DiagnosticId);
    }
}
