// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Client.Entities;

/// <summary>
/// Request struct for <see cref="DurableEntityClient.CleanEntityStorageAsync"/>.
/// </summary>
public readonly record struct CleanEntityStorageRequest
{
    /// <summary>
    /// Gets the default request parameters. The default is meant to represent
    /// "maximal" cleaning that is safe to call at all times.
    /// </summary>
    public static CleanEntityStorageRequest Default => new()
    {
        RemoveEmptyEntities = true,
        ReleaseOrphanedLocks = true,
        ContinuationToken = null,
    };

    /// <summary>
    /// Gets a value indicating whether to remove empty entities. Defaults to true.
    /// </summary>
    /// <remarks>
    /// An entity is considered empty, and is removed, if it has no state, is not locked.
    /// </remarks>
    public bool RemoveEmptyEntities { get; init; }

    /// <summary>
    /// Gets a value indicating whether to release orphaned locks or not. Defaults to true.
    /// </summary>
    /// <remarks>
    /// Locks are considered orphaned, and are released, and if the orchestration that holds them is not in state
    /// <see cref="OrchestrationRuntimeStatus.Running"/>. This hould not happen under normal circumstances, but can
    /// occur if the orchestration instance holding the lock exhibits replay nondeterminism failures, or if it is
    /// explicitly purged.
    /// </remarks>
    public bool ReleaseOrphanedLocks { get; init; }

    /// <summary>
    /// Gets the continuation token to resume a previous <see cref="CleanEntityStorageRequest"/>. Defaults to null.
    /// </summary>
    public string? ContinuationToken { get; init; }
}

/// <summary>
/// Result struct for <see cref="DurableEntityClient.CleanEntityStorageAsync"/>.
/// </summary>
public readonly record struct CleanEntityStorageResult
{
    /// <summary>
    /// Gets the number of empty entities removed.
    /// </summary>
    public int EmptyEntitiesRemoved { get; init; }

    /// <summary>
    /// Gets the number of orphaned locks that were released.
    /// </summary>
    public int OrphanedLocksReleased { get; init; }

    /// <summary>
    /// Gets the continuation token to continue the <see cref="CleanEntityStorageRequest"/>, if not null.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
