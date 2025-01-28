// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim;

/// <summary>
/// A shim client for interacting with entities backend via <see cref="IOrchestrationServiceClient"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ShimDurableEntityClient"/> class.
/// </remarks>
/// <param name="name">The name of this client.</param>
/// <param name="options">The client options..</param>
class ShimDurableEntityClient(string name, ShimDurableTaskClientOptions options) : DurableEntityClient(name)
{
    readonly ShimDurableTaskClientOptions options = Check.NotNull(options);

    EntityBackendQueries Queries => this.options.Entities.Queries!;

    DataConverter Converter => this.options.DataConverter;

    /// <inheritdoc/>
    public override async Task<CleanEntityStorageResult> CleanEntityStorageAsync(
        CleanEntityStorageRequest? request = null,
        bool continueUntilComplete = true,
        CancellationToken cancellation = default)
    {
        CleanEntityStorageRequest r = request ?? CleanEntityStorageRequest.Default;
        EntityBackendQueries.CleanEntityStorageResult result = await this.Queries.CleanEntityStorageAsync(
            new EntityBackendQueries.CleanEntityStorageRequest()
            {
                RemoveEmptyEntities = r.RemoveEmptyEntities,
                ReleaseOrphanedLocks = r.ReleaseOrphanedLocks,
                ContinuationToken = r.ContinuationToken,
            },
            cancellation);

        return new()
        {
            EmptyEntitiesRemoved = result.EmptyEntitiesRemoved,
            OrphanedLocksReleased = result.OrphanedLocksReleased,
            ContinuationToken = result.ContinuationToken,
        };
    }

    /// <inheritdoc/>
    public override AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null)
        => this.GetAllEntitiesAsync(this.Convert, filter);

    /// <inheritdoc/>
    public override AsyncPageable<EntityMetadata<T>> GetAllEntitiesAsync<T>(EntityQuery? filter = null)
        => this.GetAllEntitiesAsync(this.Convert<T>, filter);

    /// <inheritdoc/>
    public override async Task<EntityMetadata?> GetEntityAsync(
        EntityInstanceId id, bool includeState = true, CancellationToken cancellation = default)
        => this.Convert(await this.Queries.GetEntityAsync(
            new EntityId(id.Name, id.Key), includeState, false, cancellation));

    /// <inheritdoc/>
    public override async Task<EntityMetadata<T>?> GetEntityAsync<T>(
        EntityInstanceId id, bool includeState = true, CancellationToken cancellation = default)
        => this.Convert<T>(await this.Queries.GetEntityAsync(
            new EntityId(id.Name, id.Key), includeState, false, cancellation));

    /// <inheritdoc/>
    public override async Task SignalEntityAsync(
        EntityInstanceId id,
        string operationName,
        object? input = null,
        SignalEntityOptions? options = null,
        CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(id.Name);
        Check.NotNull(id.Key);

        DateTimeOffset? scheduledTime = options?.SignalTime;
        string? serializedInput = this.Converter.Serialize(input);

        EntityMessageEvent eventToSend = ClientEntityHelpers.EmitOperationSignal(
            new OrchestrationInstance() { InstanceId = id.ToString() },
            Guid.NewGuid(),
            operationName,
            serializedInput,
            EntityMessageEvent.GetCappedScheduledTime(
                DateTime.UtcNow,
                this.options.Entities.MaxSignalDelayTimeOrDefault,
                scheduledTime?.UtcDateTime));

        await this.options.Client!.SendTaskOrchestrationMessageAsync(eventToSend.AsTaskMessage());
    }

    AsyncPageable<TMetadata> GetAllEntitiesAsync<TMetadata>(
        Func<EntityBackendQueries.EntityMetadata, TMetadata> select,
        EntityQuery? filter)
        where TMetadata : notnull
    {
        bool includeState = filter?.IncludeState ?? true;
        bool includeTransient = filter?.IncludeTransient ?? false;
        string startsWith = filter?.InstanceIdStartsWith ?? string.Empty;
        DateTime? lastModifiedFrom = filter?.LastModifiedFrom?.UtcDateTime;
        DateTime? lastModifiedTo = filter?.LastModifiedTo?.UtcDateTime;

        return Pageable.Create(async (continuation, size, cancellation) =>
        {
            size ??= filter?.PageSize;
            EntityBackendQueries.EntityQueryResult result = await this.Queries.QueryEntitiesAsync(
                new EntityBackendQueries.EntityQuery()
                {
                    InstanceIdStartsWith = startsWith,
                    LastModifiedFrom = lastModifiedFrom,
                    LastModifiedTo = lastModifiedTo,
                    IncludeTransient = includeTransient,
                    IncludeState = includeState,
                    ContinuationToken = continuation,
                    PageSize = size,
                },
                cancellation);

            return new Page<TMetadata>([.. result.Results.Select(select)], result.ContinuationToken);
        });
    }

    EntityMetadata<T> Convert<T>(EntityBackendQueries.EntityMetadata metadata)
    {
        return new(
            new EntityInstanceId(metadata.EntityId.Name, metadata.EntityId.Key),
            this.Converter.Deserialize<T>(metadata.SerializedState))
            {
                LastModifiedTime = metadata.LastModifiedTime,
                BacklogQueueSize = metadata.BacklogQueueSize,
                LockedBy = metadata.LockedBy,
            };
    }

    EntityMetadata<T>? Convert<T>(EntityBackendQueries.EntityMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return this.Convert<T>(metadata.Value);
    }

    EntityMetadata Convert(EntityBackendQueries.EntityMetadata metadata)
    {
        SerializedData? data = metadata.SerializedState is null ? null : new(metadata.SerializedState, this.Converter);
        return new(new EntityInstanceId(metadata.EntityId.Name, metadata.EntityId.Key), data)
        {
            LastModifiedTime = metadata.LastModifiedTime,
            BacklogQueueSize = metadata.BacklogQueueSize,
            LockedBy = metadata.LockedBy,
        };
    }

    EntityMetadata? Convert(EntityBackendQueries.EntityMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return this.Convert(metadata.Value);
    }
}
