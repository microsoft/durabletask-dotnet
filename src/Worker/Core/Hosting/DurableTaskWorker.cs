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
    protected DurableTaskWorker(string? name, IDurableTaskFactory factory)
    {
        this.Name = name ?? Microsoft.Extensions.Options.Options.DefaultName;
        this.Factory = Check.NotNull(factory);
    }

    /// <summary>
    /// Gets the name of this worker.
    /// </summary>
    protected virtual string Name { get; }

    /// <summary>
    /// Gets the <see cref="IDurableTaskFactory" /> which has been initialized from
    /// the configured tasks during host construction.
    /// </summary>
    protected virtual IDurableTaskFactory Factory { get; }

    /// <summary>
    /// Gets or sets the exception properties provider used to enrich failure details with custom exception properties.
    /// </summary>
    protected IExceptionPropertiesProvider? ExceptionPropertiesProvider { get; set; }
}
