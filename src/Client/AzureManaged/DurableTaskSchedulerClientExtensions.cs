// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Linq;
using Azure.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Extension methods for configuring Durable Task clients to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskSchedulerClientExtensions
{
    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service.
    /// </summary>
    /// <param name="builder">The Durable Task client builder to configure.</param>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler resource. Expected to be in the format "https://{scheduler-name}.{region}.durabletask.io".</param>
    /// <param name="taskHubName">The name of the task hub resource associated with the Durable Task Scheduler resource.</param>
    /// <param name="credential">The credential used to authenticate with the Durable Task Scheduler task hub resource.</param>
    /// <param name="configure">Optional callback to dynamically configure DurableTaskSchedulerClientOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential,
        Action<DurableTaskSchedulerClientOptions>? configure = null)
    {
        ConfigureSchedulerOptions(
            builder,
            options =>
            {
                options.EndpointAddress = endpointAddress;
                options.TaskHubName = taskHubName;
                options.Credential = credential;
            },
            configure);
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The Durable Task client builder to configure.</param>
    /// <param name="connectionString">The connection string used to connect to the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to dynamically configure DurableTaskSchedulerClientOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerClientOptions>? configure = null)
    {
        var connectionOptions = DurableTaskSchedulerClientOptions.FromConnectionString(connectionString);
        ConfigureSchedulerOptions(
            builder,
            options =>
            {
                options.EndpointAddress = connectionOptions.EndpointAddress;
                options.TaskHubName = connectionOptions.TaskHubName;
                options.Credential = connectionOptions.Credential;
                options.AllowInsecureCredentials = connectionOptions.AllowInsecureCredentials;
            },
            configure);
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service using configuration options.
    /// </summary>
    /// <param name="builder">The Durable Task client builder to configure.</param>
    /// <param name="configure">Callback to configure DurableTaskSchedulerClientOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        Action<DurableTaskSchedulerClientOptions>? configure = null)
    {
        ConfigureSchedulerOptions(builder, _ => { }, configure);
    }

    static void ConfigureSchedulerOptions(
        IDurableTaskClientBuilder builder,
        Action<DurableTaskSchedulerClientOptions> initialConfig,
        Action<DurableTaskSchedulerClientOptions>? additionalConfig)
    {
        builder.Services.AddOptions<DurableTaskSchedulerClientOptions>(builder.Name)
            .Configure(initialConfig)
            .Configure(additionalConfig ?? (_ => { }))
            .ValidateDataAnnotations();

        builder.Configure(options =>
        {
            options.EnableEntitySupport = true;
        });

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<GrpcDurableTaskClientOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Configuration class that sets up gRPC channels for client options
    /// using the provided Durable Task Scheduler options.
    /// Channels are cached per configuration key and disposed when the service provider is disposed.
    /// </summary>
    sealed class ConfigureGrpcChannel : IConfigureNamedOptions<GrpcDurableTaskClientOptions>, IAsyncDisposable
    {
        readonly IOptionsMonitor<DurableTaskSchedulerClientOptions> schedulerOptions;
        readonly ConcurrentDictionary<string, Lazy<GrpcChannel>> channels = new();
        int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureGrpcChannel"/> class.
        /// </summary>
        /// <param name="schedulerOptions">Monitor for accessing the current scheduler options configuration.</param>
        public ConfigureGrpcChannel(IOptionsMonitor<DurableTaskSchedulerClientOptions> schedulerOptions)
        {
            this.schedulerOptions = schedulerOptions;
        }

        /// <summary>
        /// Configures the default named options instance.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(GrpcDurableTaskClientOptions options) => this.Configure(Options.DefaultName, options);

        /// <summary>
        /// Configures a named options instance.
        /// </summary>
        /// <param name="name">The name of the options instance to configure.</param>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(string? name, GrpcDurableTaskClientOptions options)
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(this.disposed == 1, this);
#else
            if (this.disposed == 1)
            {
                throw new ObjectDisposedException(nameof(ConfigureGrpcChannel));
            }
#endif

            string optionsName = name ?? Options.DefaultName;
            DurableTaskSchedulerClientOptions source = this.schedulerOptions.Get(optionsName);

            // Create a cache key based on the options name, endpoint, and task hub.
            // This ensures channels are reused for the same configuration
            // but separate channels are created for different configurations.
            string cacheKey = $"{optionsName}:{source.EndpointAddress}:{source.TaskHubName}";
            options.Channel = this.channels.GetOrAdd(
                cacheKey,
                _ => new Lazy<GrpcChannel>(source.CreateChannel)).Value;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 1)
            {
                return;
            }

            List<Exception>? exceptions = null;
            foreach (Lazy<GrpcChannel> channel in this.channels.Values.Where(lazy => lazy.IsValueCreated))
            {
                try
                {
                    await DisposeChannelAsync(channel.Value).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(ex);
                }
            }

            this.channels.Clear();
            GC.SuppressFinalize(this);

            if (exceptions is { Count: > 0 })
            {
                throw new AggregateException(exceptions);
            }
        }

        static async Task DisposeChannelAsync(GrpcChannel channel)
        {
            // ShutdownAsync is the graceful way to close a gRPC channel.
            using (channel)
            {
                try
                {
                    await channel.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    // Ignore expected shutdown/disposal errors
                }
            }
        }
    }
}
