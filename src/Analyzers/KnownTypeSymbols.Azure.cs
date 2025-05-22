// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Dapr.DurableTask.Analyzers;

/// <summary>
/// Provides a set of well-known types that are used by the analyzers.
/// Inspired by KnownTypeSymbols class in
/// <see href="https://github.com/dotnet/runtime/blob/2a846acb1a92e811427babe3ff3f047f98c5df02/src/libraries/System.Text.Json/gen/Helpers/KnownTypeSymbols.cs">System.Text.Json.SourceGeneration</see> source code.
/// Lazy initialization is used to avoid the the initialization of all types during class construction, since not all symbols are used by all analyzers.
/// </summary>
public sealed partial class KnownTypeSymbols
{
    INamedTypeSymbol? blobServiceClient;
    INamedTypeSymbol? blobContainerClient;
    INamedTypeSymbol? blobClient;
    INamedTypeSymbol? queueServiceClient;
    INamedTypeSymbol? queueClient;
    INamedTypeSymbol? tableServiceClient;
    INamedTypeSymbol? tableClient;
    INamedTypeSymbol? cosmosClient;
    INamedTypeSymbol? sqlConnection;

    /// <summary>
    /// Gets a BlobServiceClient type symbol.
    /// </summary>
    public INamedTypeSymbol? BlobServiceClient => this.GetOrResolveFullyQualifiedType("Azure.Storage.Blobs.BlobServiceClient", ref this.blobServiceClient);

    /// <summary>
    /// Gets a BlobContainerClient type symbol.
    /// </summary>
    public INamedTypeSymbol? BlobContainerClient => this.GetOrResolveFullyQualifiedType("Azure.Storage.Blobs.BlobContainerClient", ref this.blobContainerClient);

    /// <summary>
    /// Gets a BlobClient type symbol.
    /// </summary>
    public INamedTypeSymbol? BlobClient => this.GetOrResolveFullyQualifiedType("Azure.Storage.Blobs.BlobClient", ref this.blobClient);

    /// <summary>
    /// Gets a QueueServiceClient type symbol.
    /// </summary>
    public INamedTypeSymbol? QueueServiceClient => this.GetOrResolveFullyQualifiedType("Azure.Storage.Queues.QueueServiceClient", ref this.queueServiceClient);

    /// <summary>
    /// Gets a QueueClient type symbol.
    /// </summary>
    public INamedTypeSymbol? QueueClient => this.GetOrResolveFullyQualifiedType("Azure.Storage.Queues.QueueClient", ref this.queueClient);

    /// <summary>
    /// Gets a TableServiceClient type symbol.
    /// </summary>
    public INamedTypeSymbol? TableServiceClient => this.GetOrResolveFullyQualifiedType("Azure.Data.Tables.TableServiceClient", ref this.tableServiceClient);

    /// <summary>
    /// Gets a TableClient type symbol.
    /// </summary>
    public INamedTypeSymbol? TableClient => this.GetOrResolveFullyQualifiedType("Azure.Data.Tables.TableClient", ref this.tableClient);

    /// <summary>
    /// Gets a CosmosClient type symbol.
    /// </summary>
    public INamedTypeSymbol? CosmosClient => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Cosmos.CosmosClient", ref this.cosmosClient);

    /// <summary>
    /// Gets a SqlConnection type symbol.
    /// </summary>
    public INamedTypeSymbol? SqlConnection => this.GetOrResolveFullyQualifiedType("Microsoft.Data.SqlClient.SqlConnection", ref this.sqlConnection);
}
