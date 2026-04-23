// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
    /// </summary>
    sealed class ConfigureGrpcChannel : IConfigureNamedOptions<GrpcDurableTaskWorkerOptions>, IAsyncDisposable
    {
        readonly IOptionsMonitor<DurableTaskSchedulerWorkerOptions> schedulerOptions;
        readonly ConcurrentDictionary<string, Lazy<GrpcChannel>> channels = new();
        readonly object syncRoot = new();
        volatile int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureGrpcChannel"/> class.
        /// </summary>
        /// <param name="schedulerOptions">Monitor for accessing the current scheduler options configuration.</param>
        public ConfigureGrpcChannel(IOptionsMonitor<DurableTaskSchedulerWorkerOptions> schedulerOptions)
        {
            this.schedulerOptions = schedulerOptions;
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

            // Create a cache key that includes all properties that affect CreateChannel behavior.
            // This ensures channels are reused for the same configuration
            // but separate channels are created when any relevant property changes.
            // Use a delimiter character (\u001F) that will not appear in typical endpoint URIs.
            string credentialType = source.Credential?.GetType().FullName ?? "null";
            string cacheKey = $"{optionsName}\u001F{source.EndpointAddress}\u001F{source.TaskHubName}\u001F{source.ResourceId}\u001F{credentialType}\u001F{source.AllowInsecureCredentials}\u001F{source.WorkerId}";
            options.Channel = this.channels.GetOrAdd(
                cacheKey,
                _ => new Lazy<GrpcChannel>(source.CreateChannel, LazyThreadSafetyMode.PublicationOnly)).Value;
            options.SetChannelRecreator((oldChannel, ct) => this.RecreateChannelAsync(cacheKey, source, oldChannel, ct));
            options.ConfigureForAzureManaged();
        }

        /// <summary>
        /// Replaces the cached worker channel for the given key with a freshly created one and schedules
        /// graceful disposal of the old channel after a grace period so any in-flight RPCs that already
        /// captured the previous channel can drain.
        /// </summary>
        async Task<GrpcChannel> RecreateChannelAsync(string cacheKey, DurableTaskSchedulerWorkerOptions source, GrpcChannel oldChannel, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();

            // Worker cache keys include WorkerId, so a single worker normally owns each cached entry.
            // Recreate callbacks can outlive Configure(...), however, so still guard against disposal
            // racing with channel publication back into the shared cache owner.
            if (this.disposed == 1)
            {
                throw new ObjectDisposedException(nameof(ConfigureGrpcChannel));
            }

            // Materialize the replacement channel BEFORE publishing it so a CreateChannel failure leaves
            // the current cache entry intact.
            GrpcChannel newChannel = source.CreateChannel();
            Lazy<GrpcChannel> newLazy = new(newChannel);
            bool disposeNewChannel = false;
            GrpcChannel? cachedChannel = null;
            lock (this.syncRoot)
            {
                if (this.disposed == 1)
                {
                    disposeNewChannel = true;
                }
                else if (this.channels.TryGetValue(cacheKey, out Lazy<GrpcChannel>? currentLazy)
                    && currentLazy.IsValueCreated
                    && !ReferenceEquals(currentLazy.Value, oldChannel))
                {
                    // The cache already moved past oldChannel (for example because this callback is stale).
                    // Keep the current winner and discard the newly-created replacement.
                    disposeNewChannel = true;
                    cachedChannel = currentLazy.Value;
                }
                else
                {
                    this.channels[cacheKey] = newLazy;
                }
            }

            if (disposeNewChannel)
            {
                await DisposeChannelAsync(newChannel).ConfigureAwait(false);
                if (cachedChannel is not null)
                {
                    return cachedChannel;
                }

                throw new ObjectDisposedException(nameof(ConfigureGrpcChannel));
            }

            // Successful swap. Schedule graceful disposal of the old channel after a grace period
            // so any in-flight RPCs that already captured it can drain.
            _ = ScheduleDeferredDisposeAsync(oldChannel);
            return newChannel;
        }

        static async Task ScheduleDeferredDisposeAsync(GrpcChannel channel)
        {
            try
            {
                // Grace period to let in-flight RPCs using the previous channel complete before draining it.
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await DisposeChannelAsync(channel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                        and not StackOverflowException
                                        and not ThreadAbortException)
            {
                if (ex is not OperationCanceledException and not ObjectDisposedException)
                {
                    Trace.TraceError(
                        "Unexpected exception while deferred-disposing gRPC channel in DurableTaskSchedulerWorkerExtensions.ScheduleDeferredDisposeAsync: {0}",
                        ex);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 1)
            {
                return;
            }

            List<Lazy<GrpcChannel>> channelsToDispose;
            lock (this.syncRoot)
            {
                channelsToDispose = this.channels.Values.Where(lazy => lazy.IsValueCreated).ToList();
                this.channels.Clear();
            }

            foreach (Lazy<GrpcChannel> channel in channelsToDispose)
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
            GC.SuppressFinalize(this);
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
