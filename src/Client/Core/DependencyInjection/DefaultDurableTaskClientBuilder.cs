// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Default builder for <see cref="IDurableTaskClientBuilder" />.
/// </summary>
/// <param name="name">The name of the builder.</param>
/// <param name="services">The service collection.</param>
public class DefaultDurableTaskClientBuilder(string? name, IServiceCollection services) : IDurableTaskClientBuilder
{
    Type? buildTarget;

    /// <inheritdoc/>
    public string Name { get; } = name ?? Options.DefaultName;

    /// <inheritdoc/>
    public IServiceCollection Services { get; } = services;

    /// <inheritdoc/>
    public Type? BuildTarget
    {
        get => this.buildTarget;
        set
        {
            if (!IsValidBuildTarget(value))
            {
                throw new ArgumentException(
                    $"Type must be non-abstract and a subclass of {typeof(DurableTaskClient)}", nameof(value));
            }

            this.buildTarget = value;
        }
    }

    /// <inheritdoc/>
    public DurableTaskClient Build(IServiceProvider serviceProvider)
    {
        if (this.buildTarget is null)
        {
            throw new InvalidOperationException(
                "No valid DurableTask client target was registered. Ensure a valid client has been configured via"
                + " 'UseBuildTarget(Type target)'. An example of a valid client is '.UseGrpc()'.");
        }

        return (DurableTaskClient)ActivatorUtilities.CreateInstance(serviceProvider, this.buildTarget, this.Name);
    }

    static bool IsValidBuildTarget(Type? type)
    {
        if (type is null)
        {
            return true; // we will let you set this back to null.
        }

        return type.IsSubclassOf(typeof(DurableTaskClient)) && !type.IsAbstract;
    }
}
