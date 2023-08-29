// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using Castle.Core.Logging;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
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
        P.SignalEntityRequest request = new P.SignalEntityRequest()
        {
            InstanceId = id.ToString(),
            Name = operationName,
            Input = this.durableTaskClient.DataConverter.Serialize(input),
        };

        DateTimeOffset? scheduledTime = options?.SignalTime;
        if (scheduledTime.HasValue)
        {
            // Convert timestamps to UTC if not already UTC
            request.ScheduledTime = Timestamp.FromDateTimeOffset(scheduledTime.Value.ToUniversalTime());
        }

        // TODO this.logger.LogSomething
        await this.sidecarClient.SignalEntityAsync(request, cancellationToken: cancellation);
    }

    /// <inheritdoc/>
    public override Task<EntityMetadata?> GetEntityAsync(EntityInstanceId id, bool includeState = false, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override AsyncPageable<EntityMetadata> GetAllEntitiesAsync(EntityQuery? filter = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override Task<CleanEntityStorageResult> CleanEntityStorageAsync(CleanEntityStorageRequest request = default, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }
}
