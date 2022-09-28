// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Grpc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods for adding Durable Task support to .NET hosted services, such as ASP.NET Core hosts.
/// </summary>
public static class DurableTaskExtensions
{
    /// <summary>
    /// Adds Durable Task orchestration processing capabilities to the current application.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="registryAction">A callback that allows you to register orchestrators and activities.</param>
    /// <param name="sidecarAddress">The address of the Durable Task sidecar endpoint.</param>
    /// <returns>Returns the current <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddDurableTaskGrpcWorker(
        this IServiceCollection serviceCollection,
        Action<IDurableTaskRegistry> registryAction,
        string? sidecarAddress = null)
    {
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
}
