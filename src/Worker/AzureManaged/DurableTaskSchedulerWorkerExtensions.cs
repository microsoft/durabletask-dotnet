// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
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
    /// <param name="configure">Optional callback to dynamically configure DurableTaskSchedulerOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        builder.Services.AddOptions<DurableTaskSchedulerOptions>(builder.Name)
            .Configure(options =>
            {
                options.EndpointAddress = endpointAddress;
                options.TaskHubName = taskHubName;
                options.Credential = credential;
            })
            .Configure(configure ?? (_ => { }))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<GrpcDurableTaskWorkerOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="connectionString">The connection string used to connect to the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to dynamically configure DurableTaskSchedulerOptions.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        var connectionOptions = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        builder.Services.AddOptions<DurableTaskSchedulerOptions>(builder.Name)
            .Configure(options =>
            {
                options.EndpointAddress = connectionOptions.EndpointAddress;
                options.TaskHubName = connectionOptions.TaskHubName;
                options.Credential = connectionOptions.Credential;
            })
            .Configure(configure ?? (_ => { }))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<GrpcDurableTaskWorkerOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Internal configuration class that sets up gRPC channels for worker options
    /// using the provided Durable Task Scheduler options.
    /// </summary>
    /// <param name="schedulerOptions">Monitor for accessing the current scheduler options configuration.</param>
    internal class ConfigureGrpcChannel(IOptionsMonitor<DurableTaskSchedulerOptions> schedulerOptions) :
        IConfigureNamedOptions<GrpcDurableTaskWorkerOptions>
    {
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
            DurableTaskSchedulerOptions source = schedulerOptions.Get(name ?? Options.DefaultName);
            options.Channel = source.CreateChannel();
        }
    }
}
