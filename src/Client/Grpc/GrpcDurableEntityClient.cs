// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using DTCore = DurableTask.Core;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// The client for entities.
/// </summary>
class GrpcDurableEntityClient : DurableEntityClient
{
    readonly TaskHubSidecarServiceClient sidecarClient;
    readonly DataConverter dataConverter;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableEntityClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="sidecarClient">The client for the GRPC connection to the sidecar.</param>
    /// <param name="logger">The logger for logging client requests.</param>
    public GrpcDurableEntityClient(
        string name, DataConverter dataConverter, TaskHubSidecarServiceClient sidecarClient, ILogger logger)
        : base(name)
    {
        this.dataConverter = dataConverter;
        this.sidecarClient = sidecarClient;
        this.logger = logger;
    }

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
        Guid requestId = Guid.NewGuid();
        DateTimeOffset? scheduledTime = options?.SignalTime;

        P.SignalEntityRequest request = new()
        {
            InstanceId = id.ToString(),
            RequestId = requestId.ToString(),
            Name = operationName,
            Input = this.dataConverter.Serialize(input),
            ScheduledTime = scheduledTime?.ToTimestamp(),
            RequestTime = DateTimeOffset.UtcNow.ToTimestamp(),
        };

        if (Activity.Current is { } activity)
        {
            request.ParentTraceContext ??= new P.TraceContext();
            request.ParentTraceContext.TraceParent = activity.Id;
            request.ParentTraceContext.TraceState = activity.TraceStateString;
        }

        // TODO this.logger.LogSomething
        try
        {
            await this.sidecarClient.SignalEntityAsync(request, cancellationToken: cancellation);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.SignalEntityAsync)} operation was canceled.", e, cancellation);
        }
    }

    /// <inheritdoc/>
    public override Task<EntityMetadata?> GetEntityAsync(
        EntityInstanceId id, bool includeState = false, CancellationToken cancellation = default)
        => this.GetEntityCoreAsync(id, includeState, (e, s) => this.ToEntityMetadata(e, s), cancellation);

    /// <inheritdoc/>
    public override Task<EntityMetadata<TState>?> GetEntityAsync<TState>(
        EntityInstanceId id, bool includeState = false, CancellationToken cancellation = default)
        => this.GetEntityCoreAsync(id, includeState, (e, s) => this.ToEntityMetadata<TState>(e, s), cancellation);

    /// <inheritdoc/>
    public override AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null)
        => this.GetAllEntitiesCoreAsync(filter, (x, s) => this.ToEntityMetadata(x, s));

    /// <inheritdoc/>
    public override AsyncPageable<EntityMetadata<TState>> GetAllEntitiesAsync<TState>(EntityQuery? filter = null)
        => this.GetAllEntitiesCoreAsync(filter, (x, s) => this.ToEntityMetadata<TState>(x, s));

    /// <inheritdoc/>
    public override async Task<CleanEntityStorageResult> CleanEntityStorageAsync(
        CleanEntityStorageRequest? request = null,
        bool continueUntilComplete = true,
        CancellationToken cancellation = default)
    {
        CleanEntityStorageRequest req = request ?? CleanEntityStorageRequest.Default;
        string? continuationToken = req.ContinuationToken;
        int emptyEntitiesRemoved = 0;
        int orphanedLocksReleased = 0;

        try
        {
            do
            {
                P.CleanEntityStorageResponse response = await this.sidecarClient.CleanEntityStorageAsync(
                    new P.CleanEntityStorageRequest
                    {
                        RemoveEmptyEntities = req.RemoveEmptyEntities,
                        ReleaseOrphanedLocks = req.ReleaseOrphanedLocks,
                        ContinuationToken = continuationToken,
                    },
                    cancellationToken: cancellation);

                continuationToken = response.ContinuationToken;
                emptyEntitiesRemoved += response.EmptyEntitiesRemoved;
                orphanedLocksReleased += response.OrphanedLocksReleased;
            }
            while (continueUntilComplete && continuationToken != null);

            if (continueUntilComplete && req.RemoveEmptyEntities)
            {
                emptyEntitiesRemoved += await this.PurgeTransientEntitiesAsync(cancellation);
            }

            return new CleanEntityStorageResult
            {
                ContinuationToken = continuationToken,
                EmptyEntitiesRemoved = emptyEntitiesRemoved,
                OrphanedLocksReleased = orphanedLocksReleased,
            };
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.CleanEntityStorageAsync)} operation was canceled.", e, cancellation);
        }
    }

    async Task<TMetadata?> GetEntityCoreAsync<TMetadata>(
        EntityInstanceId id,
        bool includeState,
        Func<P.EntityMetadata, bool, TMetadata> select,
        CancellationToken cancellation)
        where TMetadata : class
    {
        Check.NotNullOrEmpty(id.Name);
        Check.NotNull(id.Key);

        P.GetEntityRequest request = new()
        {
            InstanceId = id.ToString(),
            IncludeState = includeState,
        };

        try
        {
            P.GetEntityResponse response = await this.sidecarClient
                .GetEntityAsync(request, cancellationToken: cancellation);

            return response.Exists ? select(response.Entity, includeState) : null;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.GetEntityAsync)} operation was canceled.", e, cancellation);
        }
    }

    AsyncPageable<TMetadata> GetAllEntitiesCoreAsync<TMetadata>(
        EntityQuery? filter, Func<P.EntityMetadata, bool, TMetadata> select)
        where TMetadata : class
    {
        bool includeState = filter?.IncludeState ?? true;
        bool includeTransient = filter?.IncludeTransient ?? false;
        string startsWith = filter?.InstanceIdStartsWith ?? string.Empty;
        DateTimeOffset? lastModifiedFrom = filter?.LastModifiedFrom;
        DateTimeOffset? lastModifiedTo = filter?.LastModifiedTo;

        return Pageable.Create(async (continuation, pageSize, cancellation) =>
        {
            pageSize ??= filter?.PageSize;

            try
            {
                P.QueryEntitiesResponse response = await this.sidecarClient.QueryEntitiesAsync(
                    new P.QueryEntitiesRequest
                    {
                        Query = new P.EntityQuery
                        {
                            InstanceIdStartsWith = startsWith,
                            LastModifiedFrom = lastModifiedFrom?.ToTimestamp(),
                            LastModifiedTo = lastModifiedTo?.ToTimestamp(),
                            IncludeState = includeState,
                            IncludeTransient = includeTransient,
                            PageSize = pageSize,
                            ContinuationToken = continuation ?? filter?.ContinuationToken,
                        },
                    },
                    cancellationToken: cancellation);

                IReadOnlyList<TMetadata> values = response.Entities
                    .Select(x => select(x, includeState))
                    .ToList();

                return new Page<TMetadata>(values, response.ContinuationToken);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.GetAllEntitiesAsync)} operation was canceled.", e, cancellation);
            }
        });
    }

    EntityMetadata ToEntityMetadata(P.EntityMetadata metadata, bool includeState)
    {
        var coreEntityId = DTCore.Entities.EntityId.FromString(metadata.InstanceId);
        EntityInstanceId entityId = new(coreEntityId.Name, coreEntityId.Key);
        bool hasState = !string.IsNullOrEmpty(metadata.SerializedState);

        DateTimeOffset lastModified = metadata.LastModifiedTime?.ToDateTimeOffset() ?? default;

        if (includeState && hasState)
        {
            SerializedData data = new(metadata.SerializedState!, this.dataConverter);
            return new EntityMetadata(entityId, data)
            {
                LastModifiedTime = lastModified,
                BacklogQueueSize = metadata.BacklogQueueSize,
                LockedBy = metadata.LockedBy,
            };
        }

        return new EntityMetadata(entityId, null, includesState: includeState)
        {
            LastModifiedTime = lastModified,
            BacklogQueueSize = metadata.BacklogQueueSize,
            LockedBy = metadata.LockedBy,
        };
    }

    EntityMetadata<T> ToEntityMetadata<T>(P.EntityMetadata metadata, bool includeState)
    {
        var coreEntityId = DTCore.Entities.EntityId.FromString(metadata.InstanceId);
        EntityInstanceId entityId = new(coreEntityId.Name, coreEntityId.Key);
        DateTimeOffset lastModified = metadata.LastModifiedTime?.ToDateTimeOffset() ?? default;
        bool hasState = !string.IsNullOrEmpty(metadata.SerializedState);

        if (includeState && hasState)
        {
            T? data = this.dataConverter.Deserialize<T>(metadata.SerializedState);
            return new EntityMetadata<T>(entityId, data)
            {
                LastModifiedTime = lastModified,
                BacklogQueueSize = metadata.BacklogQueueSize,
                LockedBy = metadata.LockedBy,
            };
        }

        return includeState
            ? new EntityMetadata<T>(entityId, state: default, includesState: true)
            {
                LastModifiedTime = lastModified,
                BacklogQueueSize = metadata.BacklogQueueSize,
                LockedBy = metadata.LockedBy,
            }
            : new EntityMetadata<T>(entityId)
            {
                LastModifiedTime = lastModified,
                BacklogQueueSize = metadata.BacklogQueueSize,
                LockedBy = metadata.LockedBy,
            };
    }

    /// <summary>
    /// Iterates transient entity metadata and purges empty entities to fully clean storage.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The number of entities removed.</returns>
    async Task<int> PurgeTransientEntitiesAsync(CancellationToken cancellation)
    {
        int removed = 0;
        string? continuation = null;

        do
        {
            P.QueryEntitiesResponse response = await this.sidecarClient.QueryEntitiesAsync(
                new P.QueryEntitiesRequest
                {
                    Query = new P.EntityQuery
                    {
                        IncludeState = true,
                        IncludeTransient = true,
                        ContinuationToken = continuation,
                    },
                },
                cancellationToken: cancellation);

            List<string> transientIds = response.Entities
                .Where(IsEmptyTransientEntity)
                .Select(e => e.InstanceId)
                .ToList();

            if (transientIds.Count > 0)
            {
                P.PurgeInstancesResponse purgeResponse = await this.sidecarClient.PurgeInstancesAsync(
                    new P.PurgeInstancesRequest
                    {
                        InstanceBatch = new P.InstanceBatch
                        {
                            InstanceIds = { transientIds },
                        },
                    },
                    cancellationToken: cancellation);

                removed += purgeResponse.DeletedInstanceCount;
            }

            continuation = response.ContinuationToken;
        }
        while (continuation != null);

        return removed;
    }

    /// <summary>
    /// Determines whether an entity is transient and contains no persisted state or locks.
    /// </summary>
    /// <param name="entity">The entity metadata.</param>
    /// <returns><c>true</c> when the entity is empty and transient; otherwise, <c>false</c>.</returns>
    static bool IsEmptyTransientEntity(P.EntityMetadata entity) =>
        string.IsNullOrEmpty(entity.SerializedState) && string.IsNullOrEmpty(entity.LockedBy);
}
