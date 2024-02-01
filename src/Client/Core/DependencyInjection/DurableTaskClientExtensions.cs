// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for working with <see cref="DurableTaskClient" />.
/// </summary>
public static class DurableTaskClientExtensions
{
    /// <summary>
    /// Purges orchestration instances metadata from the durable store.
    /// </summary>
    /// <param name="client">The DurableTask client.</param>
    /// <param name="createdFrom">Filter purging to orchestrations after this date.</param>
    /// <param name="createdTo">Filter purging to orchestrations before this date.</param>
    /// <param name="statuses">Filter purging to orchestrations with these statuses.</param>
    /// <param name="options">The optional options for purging the orchestration.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged,
    /// including the count of sub-orchestrations purged if any.
    /// </returns>
    public static Task<PurgeResult> PurgeInstancesAsync(
        this DurableTaskClient client,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        IEnumerable<OrchestrationRuntimeStatus>? statuses,
        PurgeInstanceOptions? options,
        CancellationToken cancellation = default)
    {
        Check.NotNull(client);
        PurgeInstancesFilter filter = new(createdFrom, createdTo, statuses);
        return client.PurgeAllInstancesAsync(filter, options, cancellation);
    }

    /// <summary>
    /// Purges orchestration instances metadata from the durable store.
    /// </summary>
    /// <param name="client">The DurableTask client.</param>
    /// <param name="createdFrom">Filter purging to orchestrations after this date.</param>
    /// <param name="createdTo">Filter purging to orchestrations before this date.</param>
    /// <param name="statuses">Filter purging to orchestrations with these statuses.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged,
    /// including the count of sub-orchestrations purged if any.
    /// </returns>
    public static Task<PurgeResult> PurgeInstancesAsync(
        this DurableTaskClient client,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        IEnumerable<OrchestrationRuntimeStatus>? statuses,
        CancellationToken cancellation = default)
        => PurgeInstancesAsync(client, createdFrom, createdTo, statuses, null, cancellation);

    /// <summary>
    /// Purges orchestration instances metadata from the durable store.
    /// </summary>
    /// <param name="client">The DurableTask client.</param>
    /// <param name="createdFrom">Filter purging to orchestrations after this date.</param>
    /// <param name="createdTo">Filter purging to orchestrations before this date.</param>
    /// <param name="options">The optional options for purging the orchestration.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged,
    /// including the count of sub-orchestrations purged if any.
    /// </returns>
    public static Task<PurgeResult> PurgeInstancesAsync(
        this DurableTaskClient client,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        PurgeInstanceOptions? options,
        CancellationToken cancellation = default)
        => PurgeInstancesAsync(client, createdFrom, createdTo, null, options, cancellation);

    /// <summary>
    /// Purges orchestration instances metadata from the durable store.
    /// </summary>
    /// <param name="client">The DurableTask client.</param>
    /// <param name="createdFrom">Filter purging to orchestrations after this date.</param>
    /// <param name="createdTo">Filter purging to orchestrations before this date.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged,
    /// including the count of sub-orchestrations purged if any.
    /// </returns>
    public static Task<PurgeResult> PurgeInstancesAsync(
        this DurableTaskClient client,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        CancellationToken cancellation = default)
        => PurgeInstancesAsync(client, createdFrom, createdTo, null, null, cancellation);
}
