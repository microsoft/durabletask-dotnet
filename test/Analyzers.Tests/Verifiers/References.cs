// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;

namespace Microsoft.DurableTask.Analyzers.Tests.Verifiers;

public static class References
{
    static readonly Lazy<ReferenceAssemblies> durableAssemblyReferences = new(() => BuildReferenceAssemblies());
    static readonly Lazy<ReferenceAssemblies> durableSdkOnlyReferences = new(() => BuildSdkOnlyReferenceAssemblies());
    static readonly Lazy<ReferenceAssemblies> durableNet80References = new(() => BuildNet80ReferenceAssemblies());

    public static ReferenceAssemblies CommonAssemblies => durableAssemblyReferences.Value;

    /// <summary>
    /// Gets assembly references for non-function SDK tests (without Azure Functions assemblies).
    /// Used to test orchestration detection in non-function scenarios.
    /// </summary>
    public static ReferenceAssemblies SdkOnlyAssemblies => durableSdkOnlyReferences.Value;

    /// <summary>
    /// Gets assembly references targeting .NET 8.0 for testing APIs only available in .NET 8+.
    /// Used for TimeProvider and other .NET 8+ specific tests.
    /// </summary>
    public static ReferenceAssemblies Net80Assemblies => durableNet80References.Value;

    static ReferenceAssemblies BuildReferenceAssemblies() => ReferenceAssemblies.Net.Net60.AddPackages([
                new PackageIdentity("Azure.Storage.Blobs", "12.17.0"),
                new PackageIdentity("Azure.Storage.Queues", "12.17.0"),
                new PackageIdentity("Azure.Data.Tables", "12.8.3"),
                new PackageIdentity("Microsoft.Azure.Cosmos", "3.39.1"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.21.0"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", "1.1.1"),
                new PackageIdentity("Microsoft.Data.SqlClient", "5.2.0"),
                ]);

    static ReferenceAssemblies BuildSdkOnlyReferenceAssemblies() => ReferenceAssemblies.Net.Net60.AddPackages([
                new PackageIdentity("Azure.Storage.Blobs", "12.17.0"),
                new PackageIdentity("Azure.Storage.Queues", "12.17.0"),
                new PackageIdentity("Azure.Data.Tables", "12.8.3"),
                new PackageIdentity("Microsoft.Azure.Cosmos", "3.39.1"),
                new PackageIdentity("Microsoft.Data.SqlClient", "5.2.0"),
                new PackageIdentity("Microsoft.DurableTask.Abstractions", "1.3.0"),
                new PackageIdentity("Microsoft.DurableTask.Worker", "1.3.0"),
                ]);

    static ReferenceAssemblies BuildNet80ReferenceAssemblies() => ReferenceAssemblies.Net.Net80.AddPackages([
                new PackageIdentity("Azure.Storage.Blobs", "12.17.0"),
                new PackageIdentity("Azure.Storage.Queues", "12.17.0"),
                new PackageIdentity("Azure.Data.Tables", "12.8.3"),
                new PackageIdentity("Microsoft.Azure.Cosmos", "3.39.1"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.21.0"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", "1.1.1"),
                new PackageIdentity("Microsoft.Data.SqlClient", "5.2.0"),
                ]);
}
