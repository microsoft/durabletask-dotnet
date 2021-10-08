//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

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
    /// <param name="builderAction">A builder callback that allows you to configure the orchestration logic.</param>
    /// <param name="sidecarAddress">The address of the Durable Task sidecar endpoint.</param>
    /// <returns>Returns the current <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddDurableTask(
        this IServiceCollection serviceCollection,
        Action<ITaskBuilder> builderAction,
        string? sidecarAddress = null)
    {
        // Register the client
        serviceCollection.AddDurableTaskClient(sidecarAddress);

        return serviceCollection.AddHostedService(services =>
        {
            TaskHubGrpcWorker.Builder workerBuilder = TaskHubGrpcWorker.CreateBuilder().UseServices(services);
            workerBuilder.AddTasks(builderAction);

            if (sidecarAddress != null)
            {
                workerBuilder.UseAddress(sidecarAddress);
            }

            TaskHubGrpcWorker worker = workerBuilder.Build();
            return worker;
        });
    }

    /// <summary>
    /// Adds a singleton <see cref="TaskHubClient"/> to the provided <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// Use this method when the the orchestration logic lives in a separate application.
    /// If your orchestration logic lives in the same application, then you should instead use the
    /// <see cref="AddDurableTask"/> method, which configures a <see cref="TaskHubClient"/> in
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
            TaskHubGrpcClient.Builder clientBuilder = TaskHubGrpcClient.CreateBuilder().UseServices(services);
            if (sidecarAddress != null)
            {
                clientBuilder.UseAddress(sidecarAddress);
            }

            return clientBuilder.Build();
        });
    }
}
