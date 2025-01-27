// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;
using Microsoft.DurableTask.Analyzers.Tests.Verifiers;

namespace Microsoft.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public abstract class MatchingAttributeBindingSpecificationTests<TAnalyzer, TCodeFix> 
    where TAnalyzer : MatchingAttributeBindingAnalyzer, new()
    where TCodeFix : MatchingAttributeBindingFixer, new()
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

        string fix = Wrapper.WrapDurableFunctionOrchestration($@"
void Method({{|#0:{this.ExpectedAttribute} {this.ExpectedType} paramName|}})
{{
}}
");

        DiagnosticResult expected = this.BuildDiagnostic().WithLocation(0).WithArguments(this.WrongType);

        await VerifyCodeFixAsync(code, expected, fix);
    }

    static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        await CSharpCodeFixVerifier<TAnalyzer, TCodeFix>.VerifyDurableTaskAnalyzerAsync(source, expected);
    }

    static async Task VerifyCodeFixAsync(string source, DiagnosticResult expected, string fix)
    {
        await CSharpCodeFixVerifier<TAnalyzer, TCodeFix>.VerifyDurableTaskCodeFixAsync(source, expected, fix);
    }

    DiagnosticResult BuildDiagnostic()
    {
        return CSharpCodeFixVerifier<TAnalyzer, TCodeFix>.Diagnostic(this.ExpectedDiagnosticId);
    }
}
