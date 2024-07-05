// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;

namespace Microsoft.DurableTask.Analyzers.Tests.Verifiers;

public static class References
{
    static readonly Lazy<ReferenceAssemblies> durableAssemblyReferences = new(() => BuildReferenceAssemblies());

    public static ReferenceAssemblies CommonAssemblies => durableAssemblyReferences.Value;

    static ReferenceAssemblies BuildReferenceAssemblies() => ReferenceAssemblies.Net.Net60.AddPackages([
                new PackageIdentity("Azure.Storage.Blobs", "12.17.0"),
                new PackageIdentity("Azure.Storage.Queues", "12.17.0"),
                new PackageIdentity("Azure.Data.Tables", "12.8.3"),
                new PackageIdentity("Microsoft.Azure.Cosmos", "3.39.1"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker", "1.21.0"),
                new PackageIdentity("Microsoft.Azure.Functions.Worker.Extensions.DurableTask", "1.1.1"),
                new PackageIdentity("Microsoft.Data.SqlClient", "5.2.0"),
                ]);
}
