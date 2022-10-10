using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Contract for building DurableTask worker.
/// </summary>
public interface IDurableTaskBuilder
{
    /// <summary>
    /// Gets the name of this builder.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the service collection associated with this builder.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Sets the target of the builder. The provided type <b>must derive from</b> <see cref="DurableTaskWorkerBase" />.
    /// This is the hosted service which will ultimately be ran on host startup.
    /// </summary>
    /// <param name="type">The target type to builder.</param>
    /// <returns>This same builder for call chaining.</returns>
    IDurableTaskBuilder SetBuildTarget(Type type);

    /// <summary>
    /// Build the hosted service which runs the worker.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <returns>The built hosted service.</returns>
    IHostedService Build(IServiceProvider serviceProvider);
}
