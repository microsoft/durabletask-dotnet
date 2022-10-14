// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.Hosting;

/// <summary>
/// Base class for durable workers.
/// </summary>
public abstract class DurableTaskWorker : BackgroundService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskWorker" /> class.
    /// </summary>
    /// <param name="name">The name of the worker.</param>
    /// <param name="factory">The durable factory.</param>
    /// <param name="options">The worker options.</param>
    protected DurableTaskWorker(
        string name, DurableTaskFactory factory, DurableTaskWorkerOptions options)
    {
        this.Name = name;
        this.Factory = factory;
        this.Options = options;
    }

    /// <summary>
    /// Gets the name of this worker.
    /// </summary>
    protected virtual string Name { get; }

    /// <summary>
    /// Gets the <see cref="DurableTaskFactory" /> which has been initialized from
    /// the configured tasks during host construction.
    /// </summary>
    protected virtual DurableTaskFactory Factory { get; }

    /// <summary>
    /// Gets the worker options.
    /// </summary>
    protected virtual DurableTaskWorkerOptions Options { get; }
}
