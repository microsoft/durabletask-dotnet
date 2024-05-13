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
            ReferenceAssemblies = ReferenceAssemblies.Net.Net60.AddPackages([
                new PackageIdentity("Azure.Storage.Blobs", "12.17.0"),
                new PackageIdentity("Azure.Storage.Queues", "12.17.0"),
                new PackageIdentity("Azure.Data.Tables", "12.8.3"),
                new PackageIdentity("Microsoft.Azure.Cosmos", "3.39.1"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.21.0"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", "1.1.1"),
                new PackageIdentity("Microsoft.Data.SqlClient", "5.2.0"),
                ]),
        };

        test.ExpectedDiagnostics.AddRange(expected);

        configureTest?.Invoke(test);

        await test.RunAsync(CancellationToken.None);
    }
}
