// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using Microsoft.DurableTask.Client.Entities;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim;

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

    /// <summary>
    /// Gets the <see cref="ShimDurableTaskEntityOptions"/> to configure entity support.
    /// </summary>
    public ShimDurableTaskEntityOptions Entities { get; } = new();
}

/// <summary>
/// Options for entities.
/// </summary>
public class ShimDurableTaskEntityOptions
{
    /// <summary>
    /// Gets or sets the <see cref="EntityBackendQueries"/> to use in the <see cref="DurableEntityClient" />.
    /// If not set manually, this will attempt to be resolved automatically by looking for
    /// <see cref="IEntityOrchestrationService"/> in the <see cref="IServiceProvider"/>.
    /// </summary>
    public EntityBackendQueries? Queries { get; set; }

    /// <summary>
    /// Gets or sets the maximum time span to use for signal delay. If not set manually, will attempt to be resolved
    /// through the service container. This will finally default to 3 days if it cannot be any other means.
    /// </summary>
    public TimeSpan? MaxSignalDelayTime { get; set; }

    /// <summary>
    /// Gets the max signal delay time.
    /// </summary>
    internal TimeSpan MaxSignalDelayTimeOrDefault => this.MaxSignalDelayTime ?? TimeSpan.FromDays(3);
}
