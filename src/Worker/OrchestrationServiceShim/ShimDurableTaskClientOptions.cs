// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Worker.Hosting;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// The shim client options.
/// </summary>
public sealed class ShimDurableTaskWorkerOptions : DurableTaskWorkerOptions
{
    /// <summary>
    /// Gets or sets the <see cref="IOrchestrationServiceClient" /> to use in the <see cref="DurableTaskWorker" />.
    /// If not manually set, this will be resolved from the <see cref="IServiceProvider" />, if available.
    /// </summary>
    public IOrchestrationService? Service { get; set; }
}
