// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Durable Task client extensions for <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures and adds a <see cref="DurableTaskClient" /> to the service collection.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="configure">The callback to configure the client.</param>
    /// <returns>The original service collection, for call chaining.</returns>
    public static IServiceCollection AddDurableTaskClient(
        this IServiceCollection services, Action<IDurableTaskClientBuilder> configure)
    {
        return services.AddDurableTaskClient(Options.DefaultName, configure);
    }

    /// <summary>
    /// Configures and adds a <see cref="DurableTaskClient" /> to the service collection.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <param name="name">Gets the name of the client to add.</param>
    /// <param name="configure">The callback to configure the client.</param>
    /// <returns>The original service collection, for call chaining.</returns>
    public static IServiceCollection AddDurableTaskClient(
        this IServiceCollection services, string name, Action<IDurableTaskClientBuilder> configure)
    {
        services.TryAddSingleton<IDurableTaskClientProvider, DefaultDurableTaskClientProvider>();
        IDurableTaskClientBuilder builder = GetBuilder(services, name, out bool added);
        configure.Invoke(builder);

        if (added)
        {
            // The added toggle logic is because we cannot use TryAddEnumerable logic as
            // we would have to dynamically compile a lambda to have it work correctly.
            ConfigureDurableOptions(services, name);
            services.AddSingleton(sp => builder.Build(sp));
        }

        return services;
    }

    static IServiceCollection ConfigureDurableOptions(IServiceCollection services, string name)
    {
        services.AddOptions<DurableTaskClientOptions>(name)
            .Configure<IServiceProvider>((opt, services) =>
            {
                // If DataConverter was not explicitly set, check to see if is available as a service.
                if (!opt.DataConverterExplicitlySet && services.GetService<DataConverter>() is DataConverter converter)
                {
                    opt.DataConverter = converter;
                }
            });

        return services;
    }

    static IDurableTaskClientBuilder GetBuilder(IServiceCollection services, string name, out bool added)
    {
        // To ensure the builders are tracked with this service collection, we use a singleton service descriptor as a
        // holder for all builders.
        ServiceDescriptor descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(BuilderContainer));

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
        readonly Dictionary<string, IDurableTaskClientBuilder> builders = new();
        readonly IServiceCollection services;

        public BuilderContainer(IServiceCollection services)
        {
            this.services = services;
        }

        public IDurableTaskClientBuilder Get(string name, out bool added)
        {
            added = false;
            if (!this.builders.TryGetValue(name, out IDurableTaskClientBuilder builder))
            {
                builder = new DefaultDurableTaskClientBuilder(name, this.services);
                this.builders[name] = builder;
                added = true;
            }

            return builder;
        }
    }
}
