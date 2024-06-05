// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Microsoft.DurableTask.Analyzers.Tests.Verifiers;

// Includes Durable Functions NuGet packages to an analyzer test and runs it
public static partial class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <inheritdoc cref="AnalyzerVerifier{TAnalyzer, TTest, TVerifier}.VerifyAnalyzerAsync(string, DiagnosticResult[])"/>
    public static async Task VerifyDurableTaskAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        await VerifyDurableTaskAnalyzerAsync(source, null, expected);
    }

    public static async Task VerifyDurableTaskAnalyzerAsync(string source, Action<Test>? configureTest = null, params DiagnosticResult[] expected)
    {
        Test test = new()
        {
            TestCode = source,
            ReferenceAssemblies = References.CommonAssemblies,
        };

        test.ExpectedDiagnostics.AddRange(expected);

        configureTest?.Invoke(test);

        await test.RunAsync(CancellationToken.None);
    }
}
