// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Client.CompatShim;

/// <summary>
/// The shim client options.
/// </summary>
public sealed class ShimDurableTaskClientOptions : DurableTaskClientOptions
{
    /// <summary>
    /// Gets or sets the <see cref="IOrchestrationServiceClient" /> to use in the <see cref="DurableTaskClient" />.
    /// If not manually set, this will be resolved from the <see cref="IServiceProvider" />, if available.
    /// </summary>
    public IOrchestrationServiceClient? Client { get; set; }
}
