// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions for <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds and configures Durable Task worker services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">The callback to configure the builder.</param>
    /// <returns>The service collection for call chaining.</returns>
    public static IServiceCollection AddDurableTaskWorker(
        this IServiceCollection services, Action<IDurableTaskBuilder> configure)
        => services.AddDurableTaskWorker(Options.Options.DefaultName, configure);

    /// <summary>
    /// Adds and configures Durable Task worker services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name of the builder to add.</param>
    /// <param name="configure">The callback to configure the builder.</param>
    /// <returns>The service collection for call chaining.</returns>
    public static IServiceCollection AddDurableTaskWorker(
        this IServiceCollection services, string name, Action<IDurableTaskBuilder> configure)
    {
        IDurableTaskBuilder builder = GetBuilder(services, name, out bool added);
        configure.Invoke(builder);

        if (added)
        {
            ConfigureDurableOptions(services, name);
            services.AddHostedService(sp => builder.Build(sp));
        }

        return services;
    }

    static IServiceCollection ConfigureDurableOptions(IServiceCollection services, string name)
    {
        services.AddOptions<DurableTaskWorkerOptions>(name)
            .Configure<IServiceProvider>((opt, services) =>
            {
                // If DataConverter was not explicitly set, check to see if is available as a service.
                if (!opt.DataConverterExplicitlySet
                    && services.GetService<DataConverter>() is DataConverter converter)
                {
                    opt.DataConverter = converter;
                }
            });

        return services;
    }

    static IDurableTaskBuilder GetBuilder(IServiceCollection services, string name, out bool added)
    {
        // To ensure the builders are tracked with this service collection, we use a singleton
        // service descriptor as a holder for all builders.
        ServiceDescriptor descriptor = services.FirstOrDefault(
            sd => sd.ImplementationType == typeof(BuilderContainer));

        if (descriptor is null)
        {
            descriptor = ServiceDescriptor.Singleton(new BuilderContainer(services));
            services.Add(descriptor);
        }

        var container = (BuilderContainer)descriptor.ImplementationInstance!;
        return container.Get(name, out added);
    }

    /// <summary>
    /// A container which is used to store and retrieve builders from within the <see cref="IServiceCollection" />.
    /// </summary>
    class BuilderContainer
    {
        readonly Dictionary<string, IDurableTaskBuilder> builders = new();
        readonly IServiceCollection services;

        public BuilderContainer(IServiceCollection services)
        {
            this.services = services;
        }

        public IDurableTaskBuilder Get(string name, out bool added)
        {
            added = false;
            if (!this.builders.TryGetValue(name, out IDurableTaskBuilder builder))
            {
                builder = new DefaultDurableTaskBuilder(name, this.services);
                this.builders[name] = builder;
                added = true;
            }

            return builder;
        }
    }
}