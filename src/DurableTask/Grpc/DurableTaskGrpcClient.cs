// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc;

/// <summary>
/// Durable Task client implementation that uses gRPC to connect to a remote "sidecar" process.
/// </summary>
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

    /// <summary>
    /// Creates a new instance of the <see cref="DurableTaskGrpcClient"/> class with default configuration.
    /// </summary>
    /// <remarks>
    /// You can use the <see cref="CreateBuilder"/> method to create client with non-default configuration.
    /// </remarks>
    /// <returns>Returns a new instance of the <see cref="DurableTaskGrpcClient"/> class.</returns>
    public static DurableTaskClient Create() => CreateBuilder().Build();

    /// <summary>
    /// Creates a new instance of the <see cref="Builder"/> class, which can be used to construct customized
    /// <see cref="DurableTaskClient"/> instances.
    /// </summary>
    /// <returns>Returns a new <see cref="Builder"/> object.</returns>
    public static Builder CreateBuilder() => new();

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Builder object for constructing customized <see cref="DurableTaskClient"/> instances.
    /// </summary>
    public sealed class Builder
    {
        internal IServiceProvider? services;
        internal ILoggerFactory? loggerFactory;
        internal DataConverter? dataConverter;
        internal IConfiguration? configuration;
        internal Channel? channel;
        internal string? hostname;
        internal int? port;

        /// <summary>
        /// Configures a logger factory to be used by the client.
        /// </summary>
        /// <remarks>
        /// Use this method to configure a logger factory explicitly. Otherwise, the client creation process will try
        /// to discover a logger factory from dependency-injected services (see the 
        /// <see cref="UseServices(IServiceProvider)"/> method).
        /// </remarks>
        /// <param name="loggerFactory">
        /// The logger factory to use or <c>null</c> to rely on default logging configuration.
        /// </param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseLoggerFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
            return this;
        }

        /// <summary>
        /// Configures a dependency-injection service provider to use when constructing the client.
        /// </summary>
        /// <param name="services">
        /// The dependency-injection service provider to configure or <c>null</c> to disable service discovery.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseServices(IServiceProvider services)
        {
            this.services = services;
            return this;
        }

        /// <summary>
        /// Explicitly configures the gRPC endpoint to connect to, including the hostname and port.
        /// </summary>
        /// <remarks>
        /// If not specified, the client creation process will try to resolve the endpoint from configuration (see
        /// the <see cref="UseConfiguration(IConfiguration)"/> method). Otherwise, 127.0.0.0:4001 will be used as the
        /// default gRPC endpoint address.
        /// </remarks>
        /// <param name="hostname">The hostname of the target gRPC endpoint. The default value is "127.0.0.1".</param>
        /// <param name="port">The port number of the target gRPC endpoint. The default value is 4001.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseAddress(string hostname, int? port = null)
        {
            this.hostname = hostname;
            this.port = port;
            return this;
        }

        /// <summary>
        /// Configures a gRPC <see cref="Channel"/> to use for communicating with the sidecar process.
        /// </summary>
        /// <remarks>
        /// This builder method allows you to provide your own gRPC channel for communicating with the Durable Task
        /// sidecar service. Channels provided using this method won't be disposed when the client is disposed.
        /// Rather, the caller remains responsible for shutting down the channel after disposing the client.
        /// </remarks>
        /// <param name="channel">The gRPC channel to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseGrpcChannel(Channel channel)
        {
            this.channel = channel;
            return this;
        }

        /// <summary>
        /// Configures a data converter to use when reading and writing orchestration data payloads.
        /// </summary>
        /// <remarks>
        /// The default behavior is to use the <see cref="JsonDataConverter"/>.
        /// </remarks>
        /// <param name="dataConverter">The data converter to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseDataConverter(DataConverter dataConverter)
        {
            this.dataConverter = dataConverter;
            return this;
        }

        /// <summary>
        /// Configures a configuration source to use when initializing the <see cref="DurableTaskClient"/> instance.
        /// </summary>
        /// <param name="configuration">The configuration source to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseConfiguration(IConfiguration configuration)
        {
            this.configuration = configuration;
            return this;
        }

        /// <summary>
        /// Initializes a new <see cref="DurableTaskClient"/> object with the settings specified in the current
        /// builder object.
        /// </summary>
        /// <returns>A new <see cref="DurableTaskClient"/> object.</returns>
        public DurableTaskClient Build() => new DurableTaskGrpcClient(this);
    }
}
