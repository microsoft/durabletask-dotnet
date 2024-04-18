// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// The default builder for durable task.
/// </summary>
/// <param name="services">The service collection for this builder.</param>
/// <param name="name">The name for this builder.</param>
public class DefaultDurableTaskWorkerBuilder(string? name, IServiceCollection services) : IDurableTaskWorkerBuilder
{
    Type? buildTarget;

    /// <inheritdoc/>
    public string Name { get; } = name ?? Extensions.Options.Options.DefaultName;

    /// <inheritdoc/>
    public IServiceCollection Services { get; } = Check.NotNull(services);

    /// <inheritdoc/>
    public Type? BuildTarget
    {
        get => this.buildTarget;
        set
        {
            if (value is not null)
            {
                Check.ConcreteType<DurableTaskWorker>(value);
            }

            this.buildTarget = value;
        }
    }

    /// <inheritdoc/>
    public IHostedService Build(IServiceProvider serviceProvider)
    {
        const string error = "No valid DurableTask worker target was registered. Ensure a valid worker has been"
            + " configured via 'UseBuildTarget(Type target)'. An example of a valid worker is '.UseGrpc()'.";
        Verify.NotNull(this.buildTarget, error);

        DurableTaskRegistry registry = serviceProvider.GetOptions<DurableTaskRegistry>(this.Name);
        return (IHostedService)ActivatorUtilities.CreateInstance(
            serviceProvider, this.buildTarget, this.Name, registry.BuildFactory());
    }
}
