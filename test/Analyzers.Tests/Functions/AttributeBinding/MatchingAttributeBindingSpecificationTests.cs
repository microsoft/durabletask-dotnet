// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;
using Microsoft.DurableTask.Analyzers.Tests.Verifiers;

namespace Microsoft.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public abstract class MatchingAttributeBindingSpecificationTests<TAnalyzer> where TAnalyzer : MatchingAttributeBindingAnalyzer, new()
{
    protected abstract string ExpectedDiagnosticId { get; }
    protected abstract string ExpectedAttribute { get; }
    protected abstract string ExpectedType { get; }
    protected virtual string WrongType { get; } = "int";

    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyAsync(code);
    }

    [Fact]
    public async Task TypeWithoutExpectedAttributeHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration($@"
void Method({this.ExpectedType} paramName)
{{
}}
");

        await VerifyAsync(code);
    }

    [Fact]
    public async Task ExpectedAttributeWithExpectedTypeHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration($@"
void Method({this.ExpectedAttribute} {this.ExpectedType} paramName)
{{
}}
");

        await VerifyAsync(code);
    }

    [Fact]
    public async Task ExpectedAttributeWithWrongTypeHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration($@"
void Method({{|#0:{this.ExpectedAttribute} {this.WrongType} paramName|}})
{{
}}
");

        DiagnosticResult expected = this.BuildDiagnostic().WithLocation(0).WithArguments(this.WrongType);

        await VerifyAsync(code, expected);
    }

    static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        await CSharpAnalyzerVerifier<TAnalyzer>.VerifyDurableTaskAnalyzerAsync(source, expected);
    }

    DiagnosticResult BuildDiagnostic()
    {
        return CSharpAnalyzerVerifier<TAnalyzer>.Diagnostic(this.ExpectedDiagnosticId);
    }
}
