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
        }, References.CommonAssemblies, configureTest);
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
        References.CommonAssemblies, configureTest);
    }

    /// <summary>
    /// Runs analyzer test with SDK-only references (without Azure Functions assemblies).
    /// Used to test orchestration detection in non-function scenarios.
    /// </summary>
    public static Task VerifySdkOnlyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        return VerifySdkOnlyAnalyzerAsync(source, null, expected);
    }

    /// <summary>
    /// Runs analyzer test with SDK-only references (without Azure Functions assemblies).
    /// Used to test orchestration detection in non-function scenarios.
    /// </summary>
    public static async Task VerifySdkOnlyAnalyzerAsync(
        string source, Action<Test>? configureTest = null, params DiagnosticResult[] expected)
    {
        await RunAsync(expected, new Test()
        {
            TestCode = source,
        }, References.SdkOnlyAssemblies, configureTest);
    }

    /// <summary>
    /// Runs code fix test with SDK-only references (without Azure Functions assemblies).
    /// Used to test orchestration detection in non-function scenarios.
    /// </summary>
    public static Task VerifySdkOnlyCodeFixAsync(
        string source, DiagnosticResult expected, string fixedSource, Action<Test>? configureTest = null)
    {
        return VerifySdkOnlyCodeFixAsync(source, [expected], fixedSource, configureTest);
    }

    /// <summary>
    /// Runs code fix test with SDK-only references (without Azure Functions assemblies).
    /// Used to test orchestration detection in non-function scenarios.
    /// </summary>
    public static async Task VerifySdkOnlyCodeFixAsync(
        string source, DiagnosticResult[] expected, string fixedSource, Action<Test>? configureTest = null)
    {
        await RunAsync(expected, new Test()
        {
            TestCode = source,
            FixedCode = fixedSource,
        },
        References.SdkOnlyAssemblies, configureTest);
    }

    /// <summary>
    /// Runs analyzer test with .NET 8.0 references for testing APIs only available in .NET 8+.
    /// Used for TimeProvider and other .NET 8+ specific tests.
    /// </summary>
    public static Task VerifyNet80AnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        return VerifyNet80AnalyzerAsync(source, null, expected);
    }

    /// <summary>
    /// Runs analyzer test with .NET 8.0 references for testing APIs only available in .NET 8+.
    /// Used for TimeProvider and other .NET 8+ specific tests.
    /// </summary>
    public static async Task VerifyNet80AnalyzerAsync(
        string source, Action<Test>? configureTest = null, params DiagnosticResult[] expected)
    {
        await RunAsync(expected, new Test()
        {
            TestCode = source,
        }, References.Net80Assemblies, configureTest);
    }

    /// <summary>
    /// Runs code fix test with .NET 8.0 references for testing APIs only available in .NET 8+.
    /// Used for TimeProvider and other .NET 8+ specific tests.
    /// </summary>
    public static Task VerifyNet80CodeFixAsync(
        string source, DiagnosticResult expected, string fixedSource, Action<Test>? configureTest = null)
    {
        return VerifyNet80CodeFixAsync(source, [expected], fixedSource, configureTest);
    }

    /// <summary>
    /// Runs code fix test with .NET 8.0 references for testing APIs only available in .NET 8+.
    /// Used for TimeProvider and other .NET 8+ specific tests.
    /// </summary>
    public static async Task VerifyNet80CodeFixAsync(
        string source, DiagnosticResult[] expected, string fixedSource, Action<Test>? configureTest = null)
    {
        await RunAsync(expected, new Test()
        {
            TestCode = source,
            FixedCode = fixedSource,
        },
        References.Net80Assemblies, configureTest);
    }

    static async Task RunAsync(DiagnosticResult[] expected, Test test, ReferenceAssemblies referenceAssemblies, Action<Test>? configureTest = null)
    {
        test.ReferenceAssemblies = referenceAssemblies;
        test.ExpectedDiagnostics.AddRange(expected);

        configureTest?.Invoke(test);

        await test.RunAsync(CancellationToken.None);
    }
}