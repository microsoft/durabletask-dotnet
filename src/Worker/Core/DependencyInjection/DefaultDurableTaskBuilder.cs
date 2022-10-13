// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// The default builder for durable task.
/// </summary>
public class DefaultDurableTaskBuilder : IDurableTaskBuilder
{
    Type? buildTarget;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultDurableTaskBuilder" />.
    /// </summary>
    /// <param name="services">The service collection for this builder.</param>
    /// <param name="name">The name for this builder.</param>
    public DefaultDurableTaskBuilder(string? name, IServiceCollection services)
    {
        this.Name = name ?? Extensions.Options.Options.DefaultName;
        this.Services = services;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IServiceCollection Services { get; }

    /// <inheritdoc/>
    public Type? BuildTarget
    {
        get => this.buildTarget;
        set
        {
            if (!value?.IsSubclassOf(typeof(DurableTaskWorker)) ?? false)
            {
                throw new ArgumentException($"Type must be subclass of {typeof(DurableTaskWorker)}", nameof(value));
            }

            this.buildTarget = value;
        }
    }

    /// <inheritdoc/>
    public IHostedService Build(IServiceProvider serviceProvider)
    {
        if (this.buildTarget is null)
        {
            throw new InvalidOperationException(
                "No valid DurableTask worker target was registered. Ensure a valid worker has been configured via"
                + " 'UseBuildTarget(Type target)'. An example of a valid worker is '.UseGrpc()'.");
        }

        DurableTaskRegistry registry = serviceProvider.GetOptions<DurableTaskRegistry>(this.Name);
        DurableTaskWorkerOptions options = serviceProvider.GetOptions<DurableTaskWorkerOptions>(this.Name);
        return (IHostedService)ActivatorUtilities.CreateInstance(
            serviceProvider, this.buildTarget, this.Name, registry.Build(), options);
    }
}
