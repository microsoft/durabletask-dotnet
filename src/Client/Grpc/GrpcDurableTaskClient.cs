// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Client.Entities;
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
    readonly DurableEntityClient? entityClient;
    AsyncDisposable asyncDisposable;

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
        this.asyncDisposable = GetCallInvoker(options, out CallInvoker callInvoker);
        this.sidecarClient = new TaskHubSidecarServiceClient(callInvoker);

        if (this.options.EnableEntitySupport)
        {
            this.entityClient = new GrpcDurableEntityClient(this.Name, this.DataConverter, this.sidecarClient, logger);
        }
    }

    /// <inheritdoc/>
    public override DurableEntityClient Entities => this.entityClient
        ?? throw new NotSupportedException($"Durable entities are disabled because {nameof(DurableTaskClientOptions)}.{nameof(DurableTaskClientOptions.EnableEntitySupport)}=false");

    DataConverter DataConverter => this.options.DataConverter;

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return this.asyncDisposable.DisposeAsync();
    }

    /// <inheritdoc/>
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default)
    {
        Check.NotEntity(this.options.EnableEntitySupport, options?.InstanceId);

        string version = string.Empty;
        if (!string.IsNullOrEmpty(orchestratorName.Version))
        {
            version = orchestratorName.Version;
        }
        else if (!string.IsNullOrEmpty(this.options.DefaultVersion))
        {
            version = this.options.DefaultVersion;
        }

        var request = new P.CreateInstanceRequest
        {
            Name = orchestratorName.Name,
            Version = version,
            InstanceId = options?.InstanceId ?? Guid.NewGuid().ToString("N"),
            Input = this.DataConverter.Serialize(input),
        };

        // Add tags to the collection
        if (request?.Tags != null && options?.Tags != null)
        {
            foreach (KeyValuePair<string, string> tag in options.Tags)
            {
                request.Tags.Add(tag.Key, tag.Value);
            }
        }

        if (Activity.Current?.Id != null || Activity.Current?.TraceStateString != null)
        {
            if (request.ParentTraceContext == null)
            {
                request.ParentTraceContext = new P.TraceContext();
            }

            if (Activity.Current?.Id != null)
            {
                request.ParentTraceContext.TraceParent = Activity.Current?.Id;
            }

            if (Activity.Current?.TraceStateString != null)
            {
                request.ParentTraceContext.TraceState = Activity.Current?.TraceStateString;
            }
        }

        DateTimeOffset? startAt = options?.StartAt;
        this.logger.SchedulingOrchestration(
            request.InstanceId,
            orchestratorName,
            sizeInBytes: request.Input != null ? Encoding.UTF8.GetByteCount(request.Input) : 0,
            startAt.GetValueOrDefault(DateTimeOffset.UtcNow));

        if (startAt.HasValue)
        {
            // Convert timestamps to UTC if not already UTC
            request.ScheduledStartTimestamp = Timestamp.FromDateTimeOffset(startAt.Value.ToUniversalTime());
        }

        P.CreateInstanceResponse? result = await this.sidecarClient.StartInstanceAsync(
            request, cancellationToken: cancellation);
        return result.InstanceId;
    }

    /// <inheritdoc/>
    public override async Task RaiseEventAsync(
        string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        Check.NotNullOrEmpty(eventName);

        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        P.RaiseEventRequest request = new()
        {
            InstanceId = instanceId,
            Name = eventName,
            Input = this.DataConverter.Serialize(eventPayload),
        };

        await this.sidecarClient.RaiseEventAsync(request, cancellationToken: cancellation);
    }

    /// <inheritdoc/>
    public override async Task TerminateInstanceAsync(
        string instanceId, TerminateInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        object? output = options?.Output;
        bool recursive = options?.Recursive ?? false;

        Check.NotNullOrEmpty(instanceId);
        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        this.logger.TerminatingInstance(instanceId);

        string? serializedOutput = this.DataConverter.Serialize(output);
        await this.sidecarClient.TerminateInstanceAsync(
            new P.TerminateRequest
            {
                InstanceId = instanceId,
                Output = serializedOutput,
                Recursive = recursive,
            },
            cancellationToken: cancellation);
    }

    /// <inheritdoc/>
    public override async Task SuspendInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        P.SuspendRequest request = new()
        {
            InstanceId = instanceId,
            Reason = reason,
        };

        try
        {
            await this.sidecarClient.SuspendInstanceAsync(request, cancellationToken: cancellation);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.SuspendInstanceAsync)} operation was canceled.", e, cancellation);
        }
    }

    /// <inheritdoc/>
    public override async Task ResumeInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        P.ResumeRequest request = new()
        {
            InstanceId = instanceId,
            Reason = reason,
        };

        try
        {
            await this.sidecarClient.ResumeInstanceAsync(request, cancellationToken: cancellation);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.ResumeInstanceAsync)} operation was canceled.", e, cancellation);
        }
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentNullException(nameof(instanceId));
        }

        P.GetInstanceResponse response = await this.sidecarClient.GetInstanceAsync(
            new P.GetInstanceRequest
            {
                InstanceId = instanceId,
                GetInputsAndOutputs = getInputsAndOutputs,
            },
            cancellationToken: cancellation);

        // REVIEW: Should we return a non-null value instead of !exists?
        if (!response.Exists)
        {
            return null;
        }

        return this.CreateMetadata(response.OrchestrationState, getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
    {
        Check.NotEntity(this.options.EnableEntitySupport, filter?.InstanceIdPrefix);

        return Pageable.Create(async (continuation, pageSize, cancellation) =>
        {
            P.QueryInstancesRequest request = new()
            {
                Query = new P.InstanceQuery
                {
                    CreatedTimeFrom = filter?.CreatedFrom?.ToTimestamp(),
                    CreatedTimeTo = filter?.CreatedTo?.ToTimestamp(),
                    FetchInputsAndOutputs = filter?.FetchInputsAndOutputs ?? false,
                    InstanceIdPrefix = filter?.InstanceIdPrefix,
                    MaxInstanceCount = pageSize ?? filter?.PageSize ?? OrchestrationQuery.DefaultPageSize,
                    ContinuationToken = continuation ?? filter?.ContinuationToken,
                },
            };

            if (filter?.Statuses is not null)
            {
                request.Query.RuntimeStatus.AddRange(filter.Statuses.Select(x => x.ToGrpcStatus()));
            }

            if (filter?.TaskHubNames is not null)
            {
                request.Query.TaskHubNames.AddRange(filter.TaskHubNames);
            }

            try
            {
                P.QueryInstancesResponse response = await this.sidecarClient.QueryInstancesAsync(
                    request, cancellationToken: cancellation);

                bool getInputsAndOutputs = filter?.FetchInputsAndOutputs ?? false;
                IReadOnlyList<OrchestrationMetadata> values = response.OrchestrationState
                    .Select(x => this.CreateMetadata(x, getInputsAndOutputs))
                    .ToList();

                return new Page<OrchestrationMetadata>(values, response.ContinuationToken);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.GetInstancesAsync)} operation was canceled.", e, cancellation);
            }
        });
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        this.logger.WaitingForInstanceStart(instanceId, getInputsAndOutputs);

        P.GetInstanceRequest request = new()
        {
            InstanceId = instanceId,
            GetInputsAndOutputs = getInputsAndOutputs,
        };

        try
        {
            P.GetInstanceResponse response = await this.sidecarClient.WaitForInstanceStartAsync(
                request, cancellationToken: cancellation);
            return this.CreateMetadata(response.OrchestrationState, getInputsAndOutputs);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.WaitForInstanceStartAsync)} operation was canceled.", e, cancellation);
        }
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotEntity(this.options.EnableEntitySupport, instanceId);

        this.logger.WaitingForInstanceCompletion(instanceId, getInputsAndOutputs);

        P.GetInstanceRequest request = new()
        {
            InstanceId = instanceId,
            GetInputsAndOutputs = getInputsAndOutputs,
        };

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                P.GetInstanceResponse response = await this.sidecarClient.WaitForInstanceCompletionAsync(
                    request, cancellationToken: cancellation);
                return this.CreateMetadata(response.OrchestrationState, getInputsAndOutputs);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(
                    $"The {nameof(this.WaitForInstanceCompletionAsync)} operation was canceled.", e, cancellation);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.DeadlineExceeded)
            {
                // Gateway timeout/deadline exceeded can happen before the request is completed. Do nothing and retry.
            }
        }

        // If the operation was cancelled in between requests, we should still throw instead of returning a null value.
        throw new OperationCanceledException($"The {nameof(this.WaitForInstanceCompletionAsync)} operation was canceled.");
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeInstanceAsync(
        string instanceId, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        bool recursive = options?.Recursive ?? false;
        this.logger.PurgingInstanceMetadata(instanceId);

        P.PurgeInstancesRequest request = new() { InstanceId = instanceId, Recursive = recursive };
        return this.PurgeInstancesCoreAsync(request, cancellation);
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeAllInstancesAsync(
        PurgeInstancesFilter filter, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        bool recursive = options?.Recursive ?? false;
        this.logger.PurgingInstances(filter);
        P.PurgeInstancesRequest request = new()
        {
            PurgeInstanceFilter = new()
            {
                CreatedTimeFrom = filter?.CreatedFrom.ToTimestamp(),
                CreatedTimeTo = filter?.CreatedTo.ToTimestamp(),
            },
            Recursive = recursive,
        };

        if (filter?.Statuses is not null)
        {
            request.PurgeInstanceFilter.RuntimeStatus.AddRange(filter.Statuses.Select(x => x.ToGrpcStatus()));
        }

        return this.PurgeInstancesCoreAsync(request, cancellation);
    }

    static AsyncDisposable GetCallInvoker(GrpcDurableTaskClientOptions options, out CallInvoker callInvoker)
    {
        if (options.Channel is GrpcChannel c)
        {
            callInvoker = c.CreateCallInvoker();
            return default;
        }

        if (options.CallInvoker is CallInvoker invoker)
        {
            callInvoker = invoker;
            return default;
        }

        c = GetChannel(options.Address);
        callInvoker = c.CreateCallInvoker();
        return new AsyncDisposable(() => new(c.ShutdownAsync()));
    }

#if NET6_0_OR_GREATER
    static GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            address = "http://localhost:4001";
        }

        return GrpcChannel.ForAddress(address);
    }
#endif

#if NETSTANDARD2_0
    static GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            address = "localhost:4001";
        }

        return new(address, ChannelCredentials.Insecure);
    }
#endif

    async Task<PurgeResult> PurgeInstancesCoreAsync(
        P.PurgeInstancesRequest request, CancellationToken cancellation = default)
    {
        try
        {
            P.PurgeInstancesResponse response = await this.sidecarClient.PurgeInstancesAsync(
                request, cancellationToken: cancellation);
            return new PurgeResult(response.DeletedInstanceCount, response.IsComplete);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(
                $"The {nameof(this.PurgeAllInstancesAsync)} operation was canceled.", e, cancellation);
        }
    }

    OrchestrationMetadata CreateMetadata(P.OrchestrationState state, bool includeInputsAndOutputs)
    {
        var metadata = new OrchestrationMetadata(state.Name, state.InstanceId)
        {
            CreatedAt = state.CreatedTimestamp.ToDateTimeOffset(),
            LastUpdatedAt = state.LastUpdatedTimestamp.ToDateTimeOffset(),
            RuntimeStatus = (OrchestrationRuntimeStatus)state.OrchestrationStatus,
            SerializedInput = state.Input,
            SerializedOutput = state.Output,
            SerializedCustomStatus = state.CustomStatus,
            FailureDetails = state.FailureDetails.ToTaskFailureDetails(),
            DataConverter = includeInputsAndOutputs ? this.DataConverter : null,
            Tags = new Dictionary<string, string>(state.Tags),
        };

        return metadata;
    }
}
