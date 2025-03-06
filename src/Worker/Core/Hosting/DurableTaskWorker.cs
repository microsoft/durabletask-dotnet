// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.Hosting;

/// <summary>
/// Base class for durable workers.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DurableTaskWorker" /> class.
/// </remarks>
/// <param name="name">The name of the worker.</param>
/// <param name="factory">The durable factory.</param>
public abstract class DurableTaskWorker(string? name, IDurableTaskFactory factory) : BackgroundService
{
    /// <summary>
    /// Gets the name of this worker.
    /// </summary>
    protected virtual string Name { get; } = name ?? Microsoft.Extensions.Options.Options.DefaultName;

    /// <summary>
    /// Gets the <see cref="IDurableTaskFactory" /> which has been initialized from
    /// the configured tasks during host construction.
    /// </summary>
    protected virtual IDurableTaskFactory Factory { get; } = Check.NotNull(factory);
}
