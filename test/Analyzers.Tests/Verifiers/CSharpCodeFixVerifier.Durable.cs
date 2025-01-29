using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Microsoft.DurableTask.Analyzers.Tests.Verifiers;

public static partial class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public static Task VerifyDurableTaskAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        return VerifyDurableTaskAnalyzerAsync(source, null, expected);
    }

    public static async Task VerifyDurableTaskAnalyzerAsync(
        string source, Action<Test>? configureTest = null, params DiagnosticResult[] expected)
    {
        await RunAsync(expected, new Test()
        {
            TestCode = source,
        }, configureTest);
    }

    public static Task VerifyDurableTaskCodeFixAsync(
        string source, DiagnosticResult expected, string fixedSource, Action<Test>? configureTest = null)
    {
        return VerifyDurableTaskCodeFixAsync(source, [expected], fixedSource, configureTest);
    }

    public static async Task VerifyDurableTaskCodeFixAsync(
        string source, DiagnosticResult[] expected, string fixedSource, Action<Test>? configureTest = null)
    {
        await RunAsync(expected, new Test()
        {
            TestCode = source,
            FixedCode = fixedSource,
        },
        configureTest);
    }

    static async Task RunAsync(DiagnosticResult[] expected, Test test, Action<Test>? configureTest = null)
    {
        test.ReferenceAssemblies = References.CommonAssemblies;
        test.ExpectedDiagnostics.AddRange(expected);

        configureTest?.Invoke(test);

        await test.RunAsync(CancellationToken.None);
    }
}