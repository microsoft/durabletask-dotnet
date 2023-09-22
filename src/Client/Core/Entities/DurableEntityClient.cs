// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// A client for interacting with entities.
/// </summary>
public abstract class DurableEntityClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableEntityClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    protected DurableEntityClient(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// Gets the name of the client.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Signals an entity to perform an operation.
    /// </summary>
    /// <param name="id">The ID of the entity to signal.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="input">The input for the operation.</param>
    /// <param name="options">The options to signal the entity with.</param>
    /// <param name="cancellation">The cancellation token to cancel enqueuing of the operation.</param>
    /// <returns>A task that completes when the message has been reliably enqueued.</returns>
    /// <remarks>This does not wait for the operation to be processed by the receiving entity.</remarks>
    public abstract Task SignalEntityAsync(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        SignalEntityOptions? options = null,
        CancellationToken cancellation = default);

    /// <summary>
    /// Signals an entity to perform an operation.
    /// </summary>
    /// <param name="id">The ID of the entity to signal.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="options">The options to signal the entity with.</param>
    /// <param name="cancellation">The cancellation token to cancel enqueuing of the operation.</param>
    /// <returns>A task that completes when the message has been reliably enqueued.</returns>
    /// <remarks>This does not wait for the operation to be processed by the receiving entity.</remarks>
    public virtual Task SignalEntityAsync(
        EntityInstanceId id,
        string operationName,
        SignalEntityOptions options,
        CancellationToken cancellation = default)
        => this.SignalEntityAsync(id, operationName, null, options, cancellation);

    /// <summary>
    /// Signals an entity to perform an operation.
    /// </summary>
    /// <param name="id">The ID of the entity to signal.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="cancellation">The cancellation token to cancel enqueuing of the operation.</param>
    /// <returns>A task that completes when the message has been reliably enqueued.</returns>
    /// <remarks>This does not wait for the operation to be processed by the receiving entity.</remarks>
    public virtual Task SignalEntityAsync(
        EntityInstanceId id,
        string operationName,
        CancellationToken cancellation)
        => this.SignalEntityAsync(id, operationName, null, null, cancellation);

    /// <summary>
    /// Tries to get the entity with ID of <paramref name="id"/>. Includes entity state by default.
    /// </summary>
    /// <param name="id">The ID of the entity to get.</param>
    /// <param name="includeState"><c>true</c> to include entity state in the response, <c>false</c> to not.</param>
    /// <param name="cancellation">The cancellation token to cancel the operation.</param>
    /// <returns>A response containing metadata describing the entity.</returns>
    public abstract Task<EntityMetadata?> GetEntityAsync(
        EntityInstanceId id, bool includeState = true, CancellationToken cancellation = default);

    /// <summary>
    /// Tries to get the entity with ID of <paramref name="id"/>. Includes entity state.
    /// </summary>
    /// <param name="id">The ID of the entity to get.</param>
    /// <param name="cancellation">The cancellation token to cancel the operation.</param>
    /// <returns>A response containing metadata describing the entity.</returns>
    public virtual Task<EntityMetadata?> GetEntityAsync(
        EntityInstanceId id, CancellationToken cancellation)
            => this.GetEntityAsync(id, includeState: true, cancellation);

    /// <summary>
    /// Tries to get the entity with ID of <paramref name="id"/>. Includes entity state by default.
    /// </summary>
    /// <typeparam name="T">The type of the entity state.</typeparam>
    /// <param name="id">The ID of the entity to get.</param>
    /// <param name="includeState"><c>true</c> to include entity state in the response, <c>false</c> to not.</param>
    /// <param name="cancellation">The cancellation token to cancel the operation.</param>
    /// <returns>A response containing metadata describing the entity.</returns>
    public abstract Task<EntityMetadata<T>?> GetEntityAsync<T>(
        EntityInstanceId id, bool includeState = true, CancellationToken cancellation = default);

    /// <summary>
    /// Tries to get the entity with ID of <paramref name="id"/>. Includes entity state.
    /// </summary>
    /// <typeparam name="T">The type of the entity state.</typeparam>
    /// <param name="id">The ID of the entity to get.</param>
    /// <param name="cancellation">The cancellation token to cancel the operation.</param>
    /// <returns>A response containing metadata describing the entity.</returns>
    public virtual Task<EntityMetadata<T>?> GetEntityAsync<T>(
        EntityInstanceId id, CancellationToken cancellation)
            => this.GetEntityAsync<T>(id, includeState: true, cancellation);

    /// <summary>
    /// Queries entity instances, optionally filtering results with <paramref name="filter"/>.
    /// </summary>
    /// <param name="filter">The optional query filter.</param>
    /// <returns>An async pageable of the query results.</returns>
    public abstract AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null);

    /// <summary>
    /// Queries entity instances, optionally filtering results with <paramref name="filter"/>.
    /// </summary>
    /// <typeparam name="T">The type of the entity state.</typeparam>
    /// <param name="filter">The optional query filter.</param>
    /// <returns>An async pageable of the query results.</returns>
    public abstract AsyncPageable<EntityMetadata<T>> GetAllEntitiesAsync<T>(EntityQuery? filter = null);

    /// <summary>
    /// Cleans entity storage. See <see cref="CleanEntityStorageRequest"/> for the different forms of cleaning available.
    /// </summary>
    /// <param name="request">The request which describes what to clean.</param>
    /// <param name="continueUntilComplete">
    /// Whether to keep going until the cleaning is complete, or to return intermediate results with a continuation token.
    /// </param>
    /// <param name="cancellation">The cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the operation is finished.</returns>
    public abstract Task<CleanEntityStorageResult> CleanEntityStorageAsync(
        CleanEntityStorageRequest request = default,
        bool continueUntilComplete = true,
        CancellationToken cancellation = default);
}
