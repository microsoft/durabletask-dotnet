// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Azure.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Client.Grpc.Internal;
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
        volatile int disposed;

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

            // Create a cache key that includes all properties that affect CreateChannel behavior.
            // This ensures channels are reused for the same configuration
            // but separate channels are created when any relevant property changes.
            // Use a delimiter character (\u001F) that will not appear in typical endpoint URIs.
            string credentialType = source.Credential?.GetType().FullName ?? "null";
            string retryOptionsKey = source.RetryOptions != null
                ? $"{source.RetryOptions.MaxRetries}|{source.RetryOptions.InitialBackoffMs}|{source.RetryOptions.MaxBackoffMs}|{source.RetryOptions.BackoffMultiplier}|{(source.RetryOptions.RetryableStatusCodes != null ? string.Join(",", source.RetryOptions.RetryableStatusCodes) : string.Empty)}"
                : "null";
            string cacheKey = $"{optionsName}\u001F{source.EndpointAddress}\u001F{source.TaskHubName}\u001F{source.ResourceId}\u001F{credentialType}\u001F{source.AllowInsecureCredentials}\u001F{retryOptionsKey}";
            options.Channel = this.channels.GetOrAdd(
                cacheKey,
                _ => new Lazy<GrpcChannel>(source.CreateChannel, LazyThreadSafetyMode.PublicationOnly)).Value;
            options.SetChannelRecreator((oldChannel, ct) => this.RecreateChannelAsync(cacheKey, source, oldChannel, ct));
        }

        /// <summary>
        /// Atomically swaps the cached channel for the given key with a freshly created one and schedules
        /// graceful disposal of the old channel after a grace period so any in-flight RPCs from peer
        /// clients can drain. Returns the currently cached channel if a peer client has already recreated it.
        /// </summary>
        async Task<GrpcChannel> RecreateChannelAsync(
            string cacheKey,
            DurableTaskSchedulerClientOptions source,
            GrpcChannel oldChannel,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();

            // Recreate callbacks can outlive Configure(...) because clients keep the delegate on their
            // options. Best-effort check for disposal before publishing anything back into the shared cache.
            if (this.disposed == 1)
            {
                throw new ObjectDisposedException(nameof(ConfigureGrpcChannel));
            }

            // Shared-cache recreation has four relevant states:
            // 1. No entry exists anymore. Create one and use it.
            // 2. The entry already materialized a different channel. A peer client already refreshed it.
            // 3. The entry still represents what this client observed. Win TryUpdate and publish the new channel.
            // 4. The entry changes between our read and TryUpdate. Lose the race, dispose ours, and reuse the winner.
            if (!this.channels.TryGetValue(cacheKey, out Lazy<GrpcChannel>? currentLazy))
            {
                // PublicationOnly avoids permanently caching a transient CreateChannel exception.
                Lazy<GrpcChannel> created = new(source.CreateChannel, LazyThreadSafetyMode.PublicationOnly);
                if (this.disposed == 1)
                {
                    throw new ObjectDisposedException(nameof(ConfigureGrpcChannel));
                }

                if (this.channels.TryAdd(cacheKey, created))
                {
                    return created.Value;
                }

                this.channels.TryGetValue(cacheKey, out currentLazy);
            }

            if (currentLazy is null)
            {
                throw new InvalidOperationException("Failed to obtain a cached gRPC channel after recreation attempt.");
            }

            // Only a materialized Lazy can be compared against oldChannel by reference. If the cache slot
            // has not created its channel yet, let TryUpdate decide whether this recreate attempt still owns it.
            if (currentLazy.IsValueCreated && !ReferenceEquals(currentLazy.Value, oldChannel))
            {
                // A peer client already swapped in a new channel; reuse it.
                return currentLazy.Value;
            }

            // Materialize the new channel BEFORE swapping the dictionary so a CreateChannel failure
            // leaves the existing entry intact. If we swapped a not-yet-materialized Lazy and then
            // CreateChannel threw, the dictionary would point to a permanently-failing Lazy and the
            // old channel would have already been queued for disposal — an unrecoverable state.
            GrpcChannel newChannel = source.CreateChannel();
            if (this.disposed == 1)
            {
                await DisposeChannelAsync(newChannel).ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(ConfigureGrpcChannel));
            }

            // The cache always stores Lazy<GrpcChannel> so the steady-state Configure path and the
            // recreate path use the same dictionary value shape. Recreate materializes first only to
            // avoid publishing a lazy that could fault before we know channel creation succeeded.
            Lazy<GrpcChannel> newLazy = new(newChannel);
            if (!this.channels.TryUpdate(cacheKey, newLazy, currentLazy))
            {
                // Lost the race. Always queue the freshly-created channel for deferred disposal so
                // it does not leak. Then return the winning entry — but if the cache slot has been
                // removed entirely (e.g. concurrent DisposeAsync cleared the dictionary), do NOT
                // hand back the doomed `newChannel`: it has already been scheduled for shutdown.
                _ = ScheduleDeferredDisposeAsync(newChannel);
                if (this.channels.TryGetValue(cacheKey, out Lazy<GrpcChannel>? winner) && winner is not null)
                {
                    return winner.Value;
                }

                throw new ObjectDisposedException(this.GetType().FullName);
            }

            if (currentLazy.IsValueCreated)
            {
                _ = ScheduleDeferredDisposeAsync(currentLazy.Value);
            }

            return newChannel;
        }

        static async Task ScheduleDeferredDisposeAsync(GrpcChannel channel)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await DisposeChannelAsync(channel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                        and not StackOverflowException
                                        and not AccessViolationException
                                        and not ThreadAbortException)
            {
                if (ex is not OperationCanceledException and not ObjectDisposedException)
                {
                    Trace.TraceError(
                        "Unexpected exception while deferred-disposing gRPC channel in DurableTaskSchedulerClientExtensions.ScheduleDeferredDisposeAsync: {0}",
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

            foreach (Lazy<GrpcChannel> channel in this.channels.Values.Where(lazy => lazy.IsValueCreated))
            {
                try
                {
                    await DisposeChannelAsync(channel.Value).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException
                                            and not StackOverflowException
                                            and not AccessViolationException
                                            and not ThreadAbortException)
                {
                    // Swallow disposal exceptions - disposal should be best-effort to ensure
                    // all channels get a chance to dispose and app shutdown is not blocked.
                    if (ex is not OperationCanceledException and not ObjectDisposedException)
                    {
                        Trace.TraceError(
                            "Unexpected exception while disposing gRPC channel in DurableTaskSchedulerClientExtensions.DisposeAsync: {0}",
                            ex);
                    }
                }
            }

            this.channels.Clear();
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
