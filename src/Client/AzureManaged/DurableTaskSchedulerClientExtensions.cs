// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
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
    /// </summary>
    /// <param name="schedulerOptions">Monitor for accessing the current scheduler options configuration.</param>
    class ConfigureGrpcChannel(IOptionsMonitor<DurableTaskSchedulerClientOptions> schedulerOptions) :
        IConfigureNamedOptions<GrpcDurableTaskClientOptions>
    {
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
            DurableTaskSchedulerClientOptions source = schedulerOptions.Get(name ?? Options.DefaultName);
            options.Channel = source.CreateChannel();
        }
    }
}
