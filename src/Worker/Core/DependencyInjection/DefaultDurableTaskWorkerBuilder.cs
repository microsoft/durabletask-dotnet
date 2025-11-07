// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// The default builder for durable task.
/// </summary>
public class DefaultDurableTaskWorkerBuilder : IDurableTaskWorkerBuilder
{
    Type? buildTarget;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDurableTaskWorkerBuilder" /> class.
    /// </summary>
    /// <param name="services">The service collection for this builder.</param>
    /// <param name="name">The name for this builder.</param>
    public DefaultDurableTaskWorkerBuilder(string? name, IServiceCollection services)
    {
        this.Name = name ?? Extensions.Options.Options.DefaultName;
        this.Services = Check.NotNull(services);
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

        // Note: Modifying any logic in this section could introduce breaking changes.
        // Do not alter the input parameter.
        return (IHostedService)ActivatorUtilities.CreateInstance(
            serviceProvider, this.buildTarget, this.Name, registry.BuildFactory());
    }
}
