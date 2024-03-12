// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker;

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
        this IServiceCollection services, Action<IDurableTaskWorkerBuilder> configure)
    {
        Check.NotNull(services);
        Check.NotNull(configure);
        return services.AddDurableTaskWorker(Options.DefaultName, configure);
    }

    /// <summary>
    /// Adds and configures Durable Task worker services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The name of the builder to add.</param>
    /// <param name="configure">The callback to configure the builder.</param>
    /// <returns>The service collection for call chaining.</returns>
    public static IServiceCollection AddDurableTaskWorker(
        this IServiceCollection services, string name, Action<IDurableTaskWorkerBuilder> configure)
    {
        Check.NotNull(services);
        Check.NotNull(name);
        Check.NotNull(configure);

        IDurableTaskWorkerBuilder builder = GetBuilder(services, name, out bool added);
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
        services.AddOptions<DurableTaskWorkerOptions>(name)
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

    static IDurableTaskWorkerBuilder GetBuilder(IServiceCollection services, string name, out bool added)
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
        return container.GetOrAdd(name, out added);
    }

    /// <summary>
    /// A container which is used to store and retrieve builders from within the <see cref="IServiceCollection" />.
    /// </summary>
    class BuilderContainer(IServiceCollection services)
    {
        readonly Dictionary<string, IDurableTaskWorkerBuilder> builders = new();

        public IDurableTaskWorkerBuilder GetOrAdd(string name, out bool added)
        {
            added = false;
            if (!this.builders.TryGetValue(name, out IDurableTaskWorkerBuilder builder))
            {
                builder = new DefaultDurableTaskWorkerBuilder(name, services);
                this.builders[name] = builder;
                added = true;
            }

            return builder;
        }
    }
}
