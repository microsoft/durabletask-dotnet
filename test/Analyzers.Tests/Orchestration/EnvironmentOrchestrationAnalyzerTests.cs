// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Orchestration.EnvironmentOrchestrationAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class EnvironmentOrchestrationAnalyzerTest
{
    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task GettingEnvironmentVariablesAreNotAllowedInAzureFunctionsOrchestrations()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    {|#0:Environment.GetEnvironmentVariable(""PATH"")|};
    {|#1:Environment.GetEnvironmentVariables()|};
    {|#2:Environment.ExpandEnvironmentVariables(""PATH"")|};
}
");
        string[] methods = [
            "Environment.GetEnvironmentVariable(string)",
            "Environment.GetEnvironmentVariables()",
            "Environment.ExpandEnvironmentVariables(string)",
        ];

        DiagnosticResult[] expected = methods.Select(
            (method, i) => BuildDiagnostic().WithLocation(i).WithArguments("Method", method, "Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task AccessingConfigurationIsNotAllowedInAzureFunctionsOrchestrations()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context, Microsoft.Extensions.Configuration.IConfiguration configuration)
{
    _ = {|#0:configuration[""PATH""]|};
    _ = {|#1:configuration.GetSection(""Section"")|};
}
");
        string[] members = [
            "IConfiguration.this[string]",
            "IConfiguration.GetSection(string)",
        ];

        DiagnosticResult[] expected = members.Select(
            (member, i) => BuildDiagnostic().WithLocation(i).WithArguments("Method", member, "Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task AccessingOptionsIsNotAllowedInAzureFunctionsOrchestrations()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
class MyOptions
{
    public string? Value { get; set; }
}

[Function(""Run"")]
void Method(
    [OrchestrationTrigger] TaskOrchestrationContext context,
    Microsoft.Extensions.Options.IOptions<MyOptions> options,
    Microsoft.Extensions.Options.IOptionsSnapshot<MyOptions> snapshot,
    Microsoft.Extensions.Options.IOptionsMonitor<MyOptions> monitor)
{
    _ = {|#0:options.Value|};
    _ = {|#1:snapshot.Value|};
    _ = {|#2:monitor.CurrentValue|};
}
");

        string[] members = [
            "IOptions<Orchestrator.MyOptions>.Value",
            "IOptions<Orchestrator.MyOptions>.Value",
            "IOptionsMonitor<Orchestrator.MyOptions>.CurrentValue",
        ];

        DiagnosticResult[] expected = members.Select(
            (member, i) => BuildDiagnostic().WithLocation(i).WithArguments("Method", member, "Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(EnvironmentOrchestrationAnalyzer.DiagnosticId);
    }
}
