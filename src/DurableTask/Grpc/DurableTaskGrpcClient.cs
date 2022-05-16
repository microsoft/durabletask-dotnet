// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc;

public class DurableTaskGrpcClient : DurableTaskClient
{
    readonly IServiceProvider services;
    readonly DataConverter dataConverter;
    readonly ILogger logger;
    readonly IConfiguration? configuration;
    readonly Channel sidecarGrpcChannel;
    readonly TaskHubSidecarServiceClient sidecarClient;
    readonly bool ownsChannel;

    bool isDisposed;

    DurableTaskGrpcClient(Builder builder)
    {
        this.services = builder.services ?? SdkUtils.EmptyServiceProvider;
        this.dataConverter = builder.dataConverter ?? this.services.GetService<DataConverter>() ?? SdkUtils.DefaultDataConverter;
        this.logger = SdkUtils.GetLogger(builder.loggerFactory ?? this.services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance);
        this.configuration = builder.configuration ?? this.services.GetService<IConfiguration>();

        if (builder.channel != null)
        {
            // Use the channel from the builder, which was given to us by the app (thus we don't own it and can't dispose it)
            this.sidecarGrpcChannel = builder.channel;
            this.ownsChannel = false;
        }
        else
        {
            // We have to create our own channel and are responsible for disposing it
            this.sidecarGrpcChannel = new Channel(
                builder.hostname ?? SdkUtils.GetSidecarHost(this.configuration),
                builder.port ?? SdkUtils.GetSidecarPort(this.configuration),
                ChannelCredentials.Insecure);
            this.ownsChannel = true;
        }
        
        this.sidecarClient = new TaskHubSidecarServiceClient(this.sidecarGrpcChannel);
    }

    public static DurableTaskClient Create() => CreateBuilder().Build();

    public static Builder CreateBuilder() => new();

    public override async ValueTask DisposeAsync()
    {
        if (!this.isDisposed)
        {
            if (this.ownsChannel)
            {
                await this.sidecarGrpcChannel.ShutdownAsync();
            }

            GC.SuppressFinalize(this);
            this.isDisposed = true;
        }
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
            Input = this.dataConverter.Serialize(input),
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
            Input = this.dataConverter.Serialize(eventPayload),
        };

        await this.sidecarClient.RaiseEventAsync(request);
    }


    public override async Task TerminateAsync(string instanceId, object? output)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            throw new ArgumentNullException(nameof(instanceId));
        }

        this.logger.TerminatingInstance(instanceId);

        string? serializedOutput = this.dataConverter.Serialize(output);
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

        return new OrchestrationMetadata(response, this.dataConverter, getInputsAndOutputs);
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
            throw new OperationCanceledException($"The {nameof(WaitForInstanceStartAsync)} operation was canceled.", e, cancellationToken);
        }

        return new OrchestrationMetadata(response, this.dataConverter, getInputsAndOutputs);
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
            throw new OperationCanceledException($"The {nameof(WaitForInstanceCompletionAsync)} operation was canceled.", e, cancellationToken);
        }

        return new OrchestrationMetadata(response, this.dataConverter, getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeInstanceMetadataAsync(string instanceId, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    public sealed class Builder
    {
        internal IServiceProvider? services;
        internal ILoggerFactory? loggerFactory;
        internal DataConverter? dataConverter;
        internal IConfiguration? configuration;
        internal Channel? channel;
        internal string? hostname;
        internal int? port;

        public Builder UseLoggerFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            return this;
        }

        public Builder UseServices(IServiceProvider services)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            return this;
        }

        public Builder UseAddress(string hostname, int? port = null)
        {
            this.hostname = hostname;
            this.port = port;
            return this;
        }

        /// <summary>
        /// Configures a <see cref="GrpcChannel"/> to use for communicating with the sidecar process.
        /// </summary>
        /// <remarks>
        /// This builder method allows you to provide your own gRPC channel for communicating with the Durable Task
        /// sidecar service. Channels provided using this method won't be disposed when the client is disposed.
        /// Rather, the caller remains responsible for shutting down the channel after disposing the client.
        /// </remarks>
        /// <param name="channel">The gRPC channel to use.</param>
        /// <returns>Returns this <see cref="Builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="channel"/> is <c>null</c>.</exception>
        public Builder UseGrpcChannel(Channel channel)
        {
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
            return this;
        }

        public Builder UseDataConverter(DataConverter dataConverter)
        {
            this.dataConverter = dataConverter ?? throw new ArgumentNullException(nameof(dataConverter));
            return this;
        }

        public Builder UseConfiguration(IConfiguration configuration)
        {
            this.configuration = configuration;
            return this;
        }

        public DurableTaskClient Build() => new DurableTaskGrpcClient(this);
    }
}
