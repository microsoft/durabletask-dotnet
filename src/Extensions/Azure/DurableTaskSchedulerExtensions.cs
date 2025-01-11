﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;


namespace Microsoft.DurableTask.Extensions.Azure;

/// <summary>
/// Extension methods for configuring Durable Task workers and clients to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskSchedulerExtensions
{
    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service.
    /// </summary>
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
            ServiceDescription.Singleton<IConfigureOptions<GrpcDurableTaskWorkerOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
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
            ServiceDescription.Singleton<IConfigureOptions<GrpcDurableTaskWorkerOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service.
    /// </summary>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        builder.Services.AddOptions<DurableTaskSchedulerOptions>(Options.DefaultName)
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
            ServiceDescription.Singleton<IConfigureOptions<GrpcDurableTaskClientOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string connectionString,
        Action<DurableTaskSchedulerOptions>? configure = null)
    {
        var connectionOptions = DurableTaskSchedulerOptions.FromConnectionString(connectionString);

        builder.Services.AddOptions<DurableTaskSchedulerOptions>(Options.DefaultName)
            .Configure(configure ?? (_ => { }))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescription.Singleton<IConfigureOptions<GrpcDurableTaskClientOptions>, ConfigureGrpcChannel>());
        builder.UseGrpc(_ => { });
    }

    // helper internal class
    class ConfigureGrpcChannel(IOptionsMonitor<DurableTaskSchedulerOptions> schedulerOptions) : IConfigureNamedOptions<GrpcDurableTaskWorkerOptions>
    {
        public void Configure(GrpcDurableTaskWorkerOptions options) => this.Configure(Options.DefaultName, options);

        public void Configure(string name, GrpcDurableTaskWorkerOptions options)
        {
            DurableTaskSchedulerOptions source = schedulerOptions.Get(name);
            options.Channel = source.CreateChannel();
        }
    }
}