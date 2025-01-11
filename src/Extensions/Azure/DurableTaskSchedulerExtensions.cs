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
    /// Configures DurableTaskScheduler options.
    /// </summary>
    public static void ConfigureDurableTaskSchedulerOptions(
        IServiceCollection services,
        Action<DurableTaskSchedulerOptions> configure)
    {
        services.Configure<DurableTaskSchedulerOptions>(Options.DefaultName, configure);
    }

    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service.
    /// </summary>
    /// <param name="builder">The worker builder to configure.</param>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler service.</param>
    /// <param name="taskHubName">The name of the task hub to connect to.</param>
    /// <param name="credential">The credential to use for authentication.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential)
    {
        DurableTaskSchedulerOptions options = new(endpointAddress, taskHubName, credential);
        builder.UseGrpc(options.GetGrpcChannel());
    }

    /// <summary>
    /// Configures Durable Task worker to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The worker builder to configure.</param>
    /// <param name="connectionString">The connection string for the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskWorkerBuilder builder,
        string connectionString)
    {
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        builder.UseGrpc(options.GetGrpcChannel());
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service.
    /// </summary>
    /// <param name="builder">The client builder to configure.</param>
    /// <param name="endpointAddress">The endpoint address of the Durable Task Scheduler service.</param>
    /// <param name="taskHubName">The name of the task hub to connect to.</param>
    /// <param name="credential">The credential to use for authentication.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string endpointAddress,
        string taskHubName,
        TokenCredential credential)
    {
        DurableTaskSchedulerOptions options = new(endpointAddress, taskHubName, credential);
        builder.UseGrpc(options.GetGrpcChannel());
    }

    /// <summary>
    /// Configures Durable Task client to use the Azure Durable Task Scheduler service using a connection string.
    /// </summary>
    /// <param name="builder">The client builder to configure.</param>
    /// <param name="connectionString">The connection string for the Durable Task Scheduler service.</param>
    /// <param name="configure">Optional callback to configure additional options.</param>
    public static void UseDurableTaskScheduler(
        this IDurableTaskClientBuilder builder,
        string connectionString)
    {
        var options = DurableTaskSchedulerOptions.FromConnectionString(connectionString);
        builder.UseGrpc(options.GetGrpcChannel());
    }
}