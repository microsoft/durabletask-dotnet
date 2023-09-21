// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using Google.Protobuf.WellKnownTypes;
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
    public GrpcDurableEntityClient(string name, DataConverter dataConverter, TaskHubSidecarServiceClient sidecarClient, ILogger logger)
        : base(name)
    {
        this.dataConverter = dataConverter;
        this.sidecarClient = sidecarClient;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override async Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null, CancellationToken cancellation = default)
    {
        Guid requestId = Guid.NewGuid();
        DateTimeOffset? scheduledTime = options?.SignalTime;

        P.SignalEntityRequest request = new()
        {
            InstanceId = id.ToString(),
            RequestId = requestId.ToString(),
            Name = operationName,
            Input = this.dataConverter.Serialize(input),
            ScheduledTime = scheduledTime?.ToTimestamp(),
        };

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
    public override async Task<EntityMetadata?> GetEntityAsync(EntityInstanceId id, bool includeState = false, CancellationToken cancellation = default)
    {
        P.GetEntityRequest request = new()
        {
            InstanceId = id.ToString(),
            IncludeState = includeState,
        };

        try
        {
            P.GetEntityResponse response = await this.sidecarClient.GetEntityAsync(request, cancellationToken: cancellation);

            return response.Exists ? this.ToEntityMetadata(response.Entity, includeState) : null;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.GetEntityAsync)} operation was canceled.", e, cancellation);
        }
    }

    /// <inheritdoc/>
    public override AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null)
    {
        bool includeState = filter?.IncludeState ?? false;
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
                            PageSize = pageSize,
                            ContinuationToken = continuation ?? filter?.ContinuationToken,
                        },
                    },
                    cancellationToken: cancellation);

                IReadOnlyList<EntityMetadata> values = response.Entities
                    .Select(x => this.ToEntityMetadata(x, includeState))
                    .ToList();

                return new Page<EntityMetadata>(values, response.ContinuationToken);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.GetAllEntitiesAsync)} operation was canceled.", e, cancellation);
            }
        });
    }

    /// <inheritdoc/>
    public override async Task<CleanEntityStorageResult> CleanEntityStorageAsync(CleanEntityStorageRequest request = default, bool continueUntilComplete = true, CancellationToken cancellation = default)
    {
        string? continuationToken = request.ContinuationToken;
        int emptyEntitiesRemoved = 0;
        int orphanedLocksReleased = 0;

        try
        {
            do
            {
                P.CleanEntityStorageResponse response = await this.sidecarClient.CleanEntityStorageAsync(
                    new P.CleanEntityStorageRequest
                    {
                        RemoveEmptyEntities = request.RemoveEmptyEntities,
                        ReleaseOrphanedLocks = request.ReleaseOrphanedLocks,
                        ContinuationToken = continuationToken,
                    },
                    cancellationToken: cancellation);

                continuationToken = response.ContinuationToken;
                emptyEntitiesRemoved += response.EmptyEntitiesRemoved;
                orphanedLocksReleased += response.OrphanedLocksReleased;
            }
            while (continueUntilComplete && continuationToken != null);

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

    EntityMetadata ToEntityMetadata(P.EntityMetadata metadata, bool includeState)
    {
        var coreEntityId = DTCore.Entities.EntityId.FromString(metadata.InstanceId);
        EntityInstanceId entityId = new(coreEntityId.Name, coreEntityId.Key);

        return new EntityMetadata(entityId)
        {
            DataConverter = includeState ? this.dataConverter : null,
            LastModifiedTime = metadata.LastModifiedTime.ToDateTimeOffset(),
            SerializedState = includeState ? metadata.SerializedState : null,
        };
    }
}
