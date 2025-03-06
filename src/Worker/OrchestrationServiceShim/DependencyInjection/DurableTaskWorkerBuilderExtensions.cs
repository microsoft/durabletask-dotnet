// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Worker.OrchestrationServiceShim;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extension methods for adding Durable Task support to .NET hosted services, such as ASP.NET Core hosts.
/// </summary>
public static class DurableTaskWorkerBuilderExtensions
{
    /// <summary>Configures the <see cref="IDurableTaskWorkerBuilder" /> to be a corker backed by a
    /// <see cref="IOrchestrationService" />.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseOrchestrationService(this IDurableTaskWorkerBuilder builder)
        => builder.UseOrchestrationService(opt => { });

    /// <summary>
    /// Configures the <see cref="IDurableTaskWorkerBuilder" /> to be a Worker backed by a
    /// <see cref="IOrchestrationService" />.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="orchestrationService">The orchestration service to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseOrchestrationService(
        this IDurableTaskWorkerBuilder builder, IOrchestrationService orchestrationService)
        => builder.UseOrchestrationService(opt =>
        {
            opt.Service = orchestrationService;
        });

    /// <summary>Configures the <see cref="IDurableTaskWorkerBuilder" /> to be a Worker backed by a
    /// <see cref="IOrchestrationService" />.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The action to configure the Worker options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseOrchestrationService(
        this IDurableTaskWorkerBuilder builder, Action<ShimDurableTaskWorkerOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);
        builder.Services.Configure(builder.Name, configure);
        builder.Services.AddOptions<ShimDurableTaskWorkerOptions>(builder.Name)
            .PostConfigure<IServiceProvider>((opt, sp) =>
            {
                if (opt.Service is not null)
                {
                    return;
                }

                // Try to resolve from service container.
                opt.Service = sp.GetService<IOrchestrationService>();
            })
            .Validate(x => x.Service is not null, "ShimDurableTaskWorkerOptions.Service must not be null.");

        return builder.UseBuildTarget<ShimDurableTaskWorker, ShimDurableTaskWorkerOptions>();
    }
}
