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
    readonly GrpcDurableTaskClient durableTaskClient;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableEntityClient"/> class.
    /// </summary>
    /// <param name="durableTaskClient">The durable Task client.</param>
    /// <param name="sidecarClient">The client for the GRPC connection to the sidecar.</param>
    /// <param name="logger">The logger for logging client requests.</param>
    public GrpcDurableEntityClient(GrpcDurableTaskClient durableTaskClient, TaskHubSidecarServiceClient sidecarClient, ILogger logger)
        : base(durableTaskClient.Name)
    {
        this.durableTaskClient = durableTaskClient;
        this.sidecarClient = sidecarClient;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override async Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null, CancellationToken cancellation = default)
    {
        Guid requestId = Guid.NewGuid();
        DateTimeOffset? scheduledTime = options?.SignalTime;

        P.SignalEntityRequest request = new P.SignalEntityRequest()
        {
            InstanceId = id.ToString(),
            Guid = Google.Protobuf.ByteString.FromStream(new MemoryStream(requestId.ToByteArray())),
            Name = operationName,
            Input = this.durableTaskClient.DataConverter.Serialize(input),
            HasScheduledTime = scheduledTime.HasValue,
            ScheduledTime = scheduledTime.HasValue ? Timestamp.FromDateTimeOffset(scheduledTime.Value.ToUniversalTime()) : default,
        };

        // TODO this.logger.LogSomething
        await this.sidecarClient.SignalEntityAsync(request, cancellationToken: cancellation);
    }

    /// <inheritdoc/>
    public override async Task<EntityMetadata?> GetEntityAsync(EntityInstanceId id, bool includeState = false, CancellationToken cancellation = default)
    {
        P.GetEntityRequest request = new P.GetEntityRequest()
        {
            InstanceId = id.ToString(),
            IncludeState = includeState,
        };

        P.GetEntityResponse response = await this.sidecarClient.GetEntityAsync(request, cancellationToken: cancellation);

        return response == null ? null : this.ToEntityMetadata(response.Entity, includeState);
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
                            LastModifiedFrom = lastModifiedFrom?.ToTimestamp() ?? default,
                            LastModifiedTo = lastModifiedTo?.ToTimestamp() ?? default,
                            IncludeState = includeState,
                            PageSize = pageSize ?? default,
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
    public override async Task<CleanEntityStorageResult> CleanEntityStorageAsync(CleanEntityStorageRequest request = default, CancellationToken cancellation = default)
    {
        P.CleanEntityStorageResponse response = await this.sidecarClient.CleanEntityStorageAsync(
            new P.CleanEntityStorageRequest
            {
                RemoveEmptyEntities = request.RemoveEmptyEntities,
                ReleaseOrphanedLocks = request.ReleaseOrphanedLocks,
            },
            cancellationToken: cancellation);

        return new CleanEntityStorageResult
        {
            EmptyEntitiesRemoved = response.EmptyEntitiesRemoved,
            OrphanedLocksReleased = response.OrphanedLocksReleased,
        };
    }

    EntityMetadata ToEntityMetadata(P.EntityMetadata metadata, bool includeState)
    {
        var coreEntityId = DTCore.Entities.EntityId.FromString(metadata.InstanceId);
        var entityId = new EntityInstanceId(coreEntityId.Name, coreEntityId.Key);

        return new EntityMetadata(entityId)
        {
            DataConverter = includeState ? this.durableTaskClient.DataConverter : null,
            LastModifiedTime = metadata.LastModifiedTime.ToDateTimeOffset(),
            SerializedState = includeState ? metadata.SerializedState : null,
        };
    }
}
