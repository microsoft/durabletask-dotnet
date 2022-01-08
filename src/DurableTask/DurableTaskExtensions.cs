// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using DurableTask.Grpc;
using Microsoft.Extensions.DependencyInjection;

namespace DurableTask;

public static class DurableTaskExtensions
{
    // TODO: Detailed remarks documentation, including example code.
    /// <summary>
    /// Adds Durable Task orchestration processing capabilities to the current application.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="registryAction">A callback that allows you to register orchestrators and activities.</param>
    /// <param name="sidecarAddress">The address of the Durable Task sidecar endpoint.</param>
    /// <returns>Returns the current <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddDurableTask(
        this IServiceCollection serviceCollection,
        Action<IDurableTaskRegistry> registryAction,
        string? sidecarAddress = null)
    {
        // Register the client
        serviceCollection.AddDurableTaskClient(sidecarAddress);

        return serviceCollection.AddHostedService(services =>
        {
            DurableTaskGrpcWorker.Builder workerBuilder = DurableTaskGrpcWorker.CreateBuilder().UseServices(services);
            workerBuilder.AddTasks(registryAction);

            if (sidecarAddress != null)
            {
                workerBuilder.UseAddress(sidecarAddress);
            }

            DurableTaskGrpcWorker worker = workerBuilder.Build();
            return worker;
        });
    }

    /// <summary>
    /// Adds a singleton <see cref="DurableTaskClient"/> to the provided <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// Use this method when the the orchestration logic lives in a separate application.
    /// If your orchestration logic lives in the same application, then you should instead use the
    /// <see cref="AddDurableTask"/> method, which configures a <see cref="DurableTaskClient"/> in
    /// addition to the orchestration logic.
    /// </remarks>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="sidecarAddress">The address of the Durable Task sidecar endpoint.</param>
    /// <returns>Returns the current <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddDurableTaskClient(
        this IServiceCollection serviceCollection,
        string? sidecarAddress = null)
    {
        return serviceCollection.AddSingleton(services =>
        {
            DurableTaskGrpcClient.Builder clientBuilder = DurableTaskGrpcClient.CreateBuilder().UseServices(services);
            if (sidecarAddress != null)
            {
                clientBuilder.UseAddress(sidecarAddress);
            }

            return clientBuilder.Build();
        });
    }
}
