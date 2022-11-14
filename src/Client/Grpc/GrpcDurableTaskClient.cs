// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// Durable Task client implementation that uses gRPC to connect to a remote "sidecar" process.
/// </summary>
public sealed class GrpcDurableTaskClient : DurableTaskClient
{
    readonly ILogger logger;
    readonly TaskHubSidecarServiceClient sidecarClient;
    readonly GrpcDurableTaskClientOptions options;
    readonly AsyncDisposable asyncDisposable;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="options">The gRPC client options.</param>
    /// <param name="logger">The logger.</param>
    [ActivatorUtilitiesConstructor]
    public GrpcDurableTaskClient(
        string name, IOptionsMonitor<GrpcDurableTaskClientOptions> options, ILogger<GrpcDurableTaskClient> logger)
        : this(name, Check.NotNull(options).Get(name), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="options">The gRPC client options.</param>
    /// <param name="logger">The logger.</param>
    public GrpcDurableTaskClient(string name, GrpcDurableTaskClientOptions options, ILogger logger)
        : base(name)
    {
        this.logger = Check.NotNull(logger);
        this.options = Check.NotNull(options);
        this.asyncDisposable = this.BuildChannel(out Channel channel);
        this.sidecarClient = new TaskHubSidecarServiceClient(channel);
    }

    DataConverter DataConverter => this.options.DataConverter;

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return this.asyncDisposable.DisposeAsync();
    }

    /// <inheritdoc/>
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        string? instanceId = null,
        object? input = null,
        DateTimeOffset? startTime = null)
    {
        var request = new P.CreateInstanceRequest
        {
            Name = orchestratorName.Name,
            Version = orchestratorName.Version,
            InstanceId = instanceId ?? Guid.NewGuid().ToString("N"),
            Input = this.DataConverter.Serialize(input),
        };

        this.logger.SchedulingOrchestration(
            request.InstanceId,
            orchestratorName,
            sizeInBytes: request.Input != null ? Encoding.UTF8.GetByteCount(request.Input) : 0,
            startTime.GetValueOrDefault(DateTimeOffset.UtcNow));

        if (startTime.HasValue)
        {
            // Convert timestamps to UTC if not already UTC
            request.ScheduledStartTimestamp = Timestamp.FromDateTimeOffset(startTime.Value.ToUniversalTime());
        }

        P.CreateInstanceResponse? result = await this.sidecarClient.StartInstanceAsync(request);
        return result.InstanceId;
    }

    /// <inheritdoc/>
    public override async Task RaiseEventAsync(string instanceId, string eventName, object? eventPayload)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentNullException(nameof(instanceId));
        }

        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentNullException(nameof(eventName));
        }

        P.RaiseEventRequest request = new()
        {
            InstanceId = instanceId,
            Name = eventName,
            Input = this.DataConverter.Serialize(eventPayload),
        };

        await this.sidecarClient.RaiseEventAsync(request);
    }

    /// <inheritdoc/>
    public override async Task TerminateAsync(string instanceId, object? output)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentNullException(nameof(instanceId));
        }

        this.logger.TerminatingInstance(instanceId);

        string? serializedOutput = this.DataConverter.Serialize(output);
        await this.sidecarClient.TerminateInstanceAsync(new P.TerminateRequest
        {
            InstanceId = instanceId,
            Output = serializedOutput,
        });
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata?> GetInstanceMetadataAsync(
        string instanceId,
        bool getInputsAndOutputs = false)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentNullException(nameof(instanceId));
        }

        P.GetInstanceResponse response = await this.sidecarClient.GetInstanceAsync(
            new P.GetInstanceRequest
            {
                InstanceId = instanceId,
                GetInputsAndOutputs = getInputsAndOutputs,
            });

        // REVIEW: Should we return a non-null value instead of !exists?
        if (!response.Exists)
        {
            return null;
        }

        return this.CreateMetadata(response.OrchestrationState, getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override AsyncPageable<OrchestrationMetadata> GetInstances(OrchestrationQuery? query = null)
    {
        return Pageable.Create(async (continuation, pageSize, cancellation) =>
        {
            P.QueryInstancesRequest request = new()
            {
                Query = new P.InstanceQuery
                {
                    CreatedTimeFrom = query?.CreatedFrom?.ToTimestamp(),
                    CreatedTimeTo = query?.CreatedTo?.ToTimestamp(),
                    FetchInputsAndOutputs = query?.FetchInputsAndOutputs ?? false,
                    InstanceIdPrefix = query?.InstanceIdPrefix,
                    MaxInstanceCount = pageSize ?? query?.PageSize ?? OrchestrationQuery.DefaultPageSize,
                    ContinuationToken = continuation ?? query?.ContinuationToken,
                },
            };

            if (query?.Statuses is not null)
            {
                request.Query.RuntimeStatus.AddRange(query.Statuses.Select(x => x.ToGrpcStatus()));
            }

            if (query?.TaskHubNames is not null)
            {
                request.Query.TaskHubNames.AddRange(query.TaskHubNames);
            }

            try
            {
                P.QueryInstancesResponse response = await this.sidecarClient.QueryInstancesAsync(
                    request, cancellationToken: cancellation);

                bool getInputsAndOutputs = query?.FetchInputsAndOutputs ?? false;
                IReadOnlyList<OrchestrationMetadata> values = response.OrchestrationState
                    .Select(x => this.CreateMetadata(x, getInputsAndOutputs))
                    .ToList();

                return new Page<OrchestrationMetadata>(values, response.ContinuationToken);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.GetInstances)} operation was canceled.", e, cancellation);
            }
        });
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId,
        CancellationToken cancellationToken,
        bool getInputsAndOutputs = false)
    {
        this.logger.WaitingForInstanceStart(instanceId, getInputsAndOutputs);

        P.GetInstanceRequest request = new()
        {
            InstanceId = instanceId,
            GetInputsAndOutputs = getInputsAndOutputs,
        };

        P.GetInstanceResponse response;
        try
        {
            response = await this.sidecarClient.WaitForInstanceStartAsync(
                request,
                cancellationToken: cancellationToken);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.WaitForInstanceStartAsync)} operation was canceled.", e, cancellationToken);
        }

        return this.CreateMetadata(response.OrchestrationState, getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId,
        CancellationToken cancellationToken,
        bool getInputsAndOutputs = false)
    {
        this.logger.WaitingForInstanceCompletion(instanceId, getInputsAndOutputs);

        P.GetInstanceRequest request = new()
        {
            InstanceId = instanceId,
            GetInputsAndOutputs = getInputsAndOutputs,
        };

        P.GetInstanceResponse response;
        try
        {
            response = await this.sidecarClient.WaitForInstanceCompletionAsync(
                request,
                cancellationToken: cancellationToken);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.WaitForInstanceCompletionAsync)} operation was canceled.", e, cancellationToken);
        }

        return this.CreateMetadata(response.OrchestrationState, getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeInstanceMetadataAsync(
        string instanceId, CancellationToken cancellation = default)
    {
        this.logger.PurgingInstanceMetadata(instanceId);

        P.PurgeInstancesRequest request = new() { InstanceId = instanceId };
        return this.PurgeInstancesCoreAsync(request, cancellation);
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeInstancesAsync(
        PurgeInstancesFilter filter, CancellationToken cancellation = default)
    {
        this.logger.PurgingInstances(filter);
        P.PurgeInstancesRequest request = new()
        {
            PurgeInstanceFilter = new()
            {
                CreatedTimeFrom = filter?.CreatedFrom.ToTimestamp(),
                CreatedTimeTo = filter?.CreatedTo.ToTimestamp(),
            },
        };

        if (filter?.Statuses is not null)
        {
            request.PurgeInstanceFilter.RuntimeStatus.AddRange(filter.Statuses.Select(x => x.ToGrpcStatus()));
        }

        return this.PurgeInstancesCoreAsync(request, cancellation);
    }

    async Task<PurgeResult> PurgeInstancesCoreAsync(
        P.PurgeInstancesRequest request, CancellationToken cancellation = default)
    {
        try
        {
            P.PurgeInstancesResponse response = await this.sidecarClient.PurgeInstancesAsync(
                request, cancellationToken: cancellation);
            return new PurgeResult(response.DeletedInstanceCount);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.PurgeInstancesAsync)} operation was canceled.", e, cancellation);
        }
    }

    OrchestrationMetadata CreateMetadata(P.OrchestrationState state, bool includeInputsAndOutputs)
    {
        return new(state.Name, state.InstanceId)
        {
            CreatedAt = state.CreatedTimestamp.ToDateTimeOffset(),
            LastUpdatedAt = state.LastUpdatedTimestamp.ToDateTimeOffset(),
            RuntimeStatus = (OrchestrationRuntimeStatus)state.OrchestrationStatus,
            SerializedInput = state.Input,
            SerializedOutput = state.Output,
            SerializedCustomStatus = state.CustomStatus,
            FailureDetails = ProtoUtils.ConvertTaskFailureDetails(state.FailureDetails),
            DataConverter = includeInputsAndOutputs ? this.DataConverter : null,
        };
    }

    AsyncDisposable BuildChannel(out Channel channel)
    {
        if (this.options.Channel is Channel c)
        {
            channel = c;
            return default;
        }

        string address = string.IsNullOrEmpty(this.options.Address) ? "127.0.0.1:4001" : this.options.Address!;

        // TODO: use SSL channel by default?
        c = new(address, ChannelCredentials.Insecure);
        channel = c;
        return new AsyncDisposable(async () => await c.ShutdownAsync());
    }
}
