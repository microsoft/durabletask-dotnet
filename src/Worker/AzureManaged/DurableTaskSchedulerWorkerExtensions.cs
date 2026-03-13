// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Azure.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.AzureManaged;

/// <summary>
/// Extension methods for configuring Durable Task workers to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskSchedulerWorkerExtensions
{
    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler resource. Expected to be in the format "https://{scheduler-name}.{region}.durabletask.io".</param>
    /// <param name="taskHubName">The name of the task hub resource associated with the Durable Task Scheduler resource.</param>
    /// <param name="credential">The credential used to authenticate with the Durable Task Scheduler task hub resource.</param>
    /// <param name="configure">Optional callback to dynamically configure DurableTaskSchedulerWorkerOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential,
        Action<DurableTaskSchedulerWorkerOptions>? configure = null)
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
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="connectionString">The connection string used to connect to the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to dynamically configure DurableTaskSchedulerWorkerOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerWorkerOptions>? configure = null)
    {
        var connectionOptions = DurableTaskSchedulerWorkerOptions.FromConnectionString(connectionString);
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
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service using configuration options.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="configure">Callback to configure DurableTaskSchedulerWorkerOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        Action<DurableTaskSchedulerWorkerOptions>? configure = null)
    {
        ConfigureSchedulerOptions(builder, _ => { }, configure);
    }

    static void ConfigureSchedulerOptions(
        IDurableTaskWorkerBuilder builder,
        Action<DurableTaskSchedulerWorkerOptions> initialConfig,
        Action<DurableTaskSchedulerWorkerOptions>? additionalConfig)
    {
        builder.Services.AddOptions<DurableTaskSchedulerWorkerOptions>(builder.Name)
            .Configure(initialConfig)
            .Configure(additionalConfig ?? (_ => { }))
            .ValidateDataAnnotations();

        builder.Services.AddOptions<DurableTaskWorkerOptions>(builder.Name)
           .Configure(options =>
           {
               options.EnableEntitySupport = true;
           });

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<GrpcDurableTaskWorkerOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Configuration class that sets up gRPC channels for worker options
    /// using the provided Durable Task Scheduler options.
    /// Channels are cached per configuration key and disposed when the service provider is disposed.
    /// When scheduler options change, cached channels are invalidated and disposed.
    /// </summary>
    sealed class ConfigureGrpcChannel : IConfigureNamedOptions<GrpcDurableTaskWorkerOptions>, IAsyncDisposable
    {
        readonly IOptionsMonitor<DurableTaskSchedulerWorkerOptions> schedulerOptions;
        readonly IOptionsMonitorCache<GrpcDurableTaskWorkerOptions> grpcOptionsCache;
        readonly ConcurrentDictionary<string, Lazy<GrpcChannel>> channels = new();
        readonly ConcurrentDictionary<string, string> cacheKeysByOptionsName = new();
        readonly IDisposable? onChangeSubscription;
        volatile int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureGrpcChannel"/> class.
        /// </summary>
        /// <param name="schedulerOptions">Monitor for accessing the current scheduler options configuration.</param>
        /// <param name="grpcOptionsCache">Cache for gRPC worker options, used to invalidate stale entries on options change.</param>
        public ConfigureGrpcChannel(
            IOptionsMonitor<DurableTaskSchedulerWorkerOptions> schedulerOptions,
            IOptionsMonitorCache<GrpcDurableTaskWorkerOptions> grpcOptionsCache)
        {
            this.schedulerOptions = schedulerOptions;
            this.grpcOptionsCache = grpcOptionsCache;
            this.onChangeSubscription = schedulerOptions.OnChange(this.OnSchedulerOptionsChanged);
        }

        /// <summary>
        /// Configures the default named options instance.
        /// </summary>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(GrpcDurableTaskWorkerOptions options) => this.Configure(Options.DefaultName, options);

        /// <summary>
        /// Configures a named options instance.
        /// </summary>
        /// <param name="name">The name of the options instance to configure.</param>
        /// <param name="options">The options instance to configure.</param>
        public void Configure(string? name, GrpcDurableTaskWorkerOptions options)
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
            DurableTaskSchedulerWorkerOptions source = this.schedulerOptions.Get(optionsName);

            string cacheKey = ComputeCacheKey(optionsName, source);
            this.cacheKeysByOptionsName[optionsName] = cacheKey;

            options.Channel = this.channels.GetOrAdd(
                cacheKey,
                _ => new Lazy<GrpcChannel>(source.CreateChannel)).Value;
            options.ConfigureForAzureManaged();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 1)
            {
                return;
            }

            this.onChangeSubscription?.Dispose();

            foreach (Lazy<GrpcChannel> channel in this.channels.Values.Where(lazy => lazy.IsValueCreated))
            {
                try
                {
                    await DisposeChannelAsync(channel.Value).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException
                                            and not StackOverflowException
                                            and not ThreadAbortException)
                {
                    // Swallow disposal exceptions - disposal should be best-effort to ensure
                    // all channels get a chance to dispose and app shutdown is not blocked.
                    if (ex is not OperationCanceledException and not ObjectDisposedException)
                    {
                        Trace.TraceError(
                            "Unexpected exception while disposing gRPC channel in DurableTaskSchedulerWorkerExtensions.DisposeAsync: {0}",
                            ex);
                    }
                }
            }

            this.channels.Clear();
            GC.SuppressFinalize(this);
        }

        void OnSchedulerOptionsChanged(DurableTaskSchedulerWorkerOptions options, string? name)
        {
            if (this.disposed == 1)
            {
                return;
            }

            string optionsName = name ?? Options.DefaultName;
            string newCacheKey = ComputeCacheKey(optionsName, options);

            if (this.cacheKeysByOptionsName.TryGetValue(optionsName, out string? oldCacheKey) && oldCacheKey != newCacheKey)
            {
                if (this.channels.TryRemove(oldCacheKey, out Lazy<GrpcChannel>? oldChannel) && oldChannel.IsValueCreated)
                {
                    DisposeChannelInBackground(oldChannel.Value);
                }

                this.cacheKeysByOptionsName[optionsName] = newCacheKey;
                this.grpcOptionsCache.TryRemove(optionsName);
            }
        }

        static string ComputeCacheKey(string optionsName, DurableTaskSchedulerWorkerOptions source)
        {
            // Create a cache key that includes all properties that affect CreateChannel behavior.
            // This ensures channels are reused for the same configuration
            // but separate channels are created when any relevant property changes.
            // Use a delimiter character (\u001F) that will not appear in typical endpoint URIs.
            string credentialType = source.Credential?.GetType().FullName ?? "null";
            return $"{optionsName}\u001F{source.EndpointAddress}\u001F{source.TaskHubName}\u001F{source.ResourceId}\u001F{credentialType}\u001F{source.AllowInsecureCredentials}\u001F{source.WorkerId}";
        }

        static void DisposeChannelInBackground(GrpcChannel channel)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await DisposeChannelAsync(channel).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException
                                            and not StackOverflowException
                                            and not ThreadAbortException)
                {
                    if (ex is not OperationCanceledException and not ObjectDisposedException)
                    {
                        Trace.TraceError(
                            "Unexpected exception while disposing stale gRPC channel in DurableTaskSchedulerWorkerExtensions: {0}",
                            ex);
                    }
                }
            });
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
