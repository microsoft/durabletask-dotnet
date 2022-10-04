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
    /// Adds a singleton <see cref="DurableTaskClient"/> to the provided <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="sidecarAddress">The address of the Durable Task sidecar endpoint.</param>
    /// <returns>Returns the current <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddDurableTaskGrpcClient(
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
