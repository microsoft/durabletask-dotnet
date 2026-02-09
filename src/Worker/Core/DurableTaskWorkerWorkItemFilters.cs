// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A class that represents work item filters for a Durable Task Worker. These filters are passed to the backend
/// and only work items matching the filters will be processed by the worker. If no filters are provided,
/// the worker will process all work items.
/// </summary>
public class DurableTaskWorkerWorkItemFilters
{
    /// <summary>
    /// Gets or sets the orchestration filters.
    /// </summary>
    public IReadOnlyList<OrchestrationFilter> Orchestrations { get; set; } = [];

    /// <summary>
    /// Gets or sets the activity filters.
    /// </summary>
    public IReadOnlyList<ActivityFilter> Activities { get; set; } = [];

    /// <summary>
    /// Gets or sets the entity filters.
    /// </summary>
    public IReadOnlyList<EntityFilter> Entities { get; set; } = [];

    /// <summary>
    /// Creates a new instance of the <see cref="DurableTaskWorkerWorkItemFilters"/> class.
    /// </summary>
    /// <param name="registry"><see cref="DurableTaskRegistry"/> to construct the filter from.</param>
    /// <param name="workerOptions"><see cref="DurableTaskWorkerOptions"/> that optionally provides versioning information.</param>
    /// <returns>A new instance of <see cref="DurableTaskWorkerWorkItemFilters"/> constructed from the provided registry.</returns>
    internal static DurableTaskWorkerWorkItemFilters FromDurableTaskRegistry(DurableTaskRegistry registry, DurableTaskWorkerOptions? workerOptions)
    {
        // TODO: Support multiple versions per orchestration/activity. For now, grab the worker version from the options.
        return new DurableTaskWorkerWorkItemFilters
        {
            Orchestrations = registry.Orchestrators.Select(orchestration => new OrchestrationFilter
            {
                Name = orchestration.Key,
                Versions = workerOptions?.Versioning != null ? [workerOptions.Versioning.DefaultVersion] : [],
            }).ToList(),
            Activities = registry.Activities.Select(activity => new ActivityFilter
            {
                Name = activity.Key,
                Versions = workerOptions?.Versioning != null ? [workerOptions.Versioning.DefaultVersion] : [],
            }).ToList(),
            Entities = registry.Entities.Select(entity => new EntityFilter
            {
                // Entity names are normalized to lowercase in the backend.
                Name = entity.Key.ToString().ToLowerInvariant(),
            }).ToList(),
        };
    }

    /// <summary>
    /// Specifies an orchestration filter.
    /// </summary>
    public struct OrchestrationFilter
    {
        /// <summary>
        /// Gets or initializes the name of the orchestration to filter.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets or initializes the versions of the orchestration to filter.
        /// </summary>
        public IReadOnlyList<string> Versions { get; init; }
    }

    /// <summary>
    /// Specifies an activity filter.
    /// </summary>
    public struct ActivityFilter
    {
        /// <summary>
        /// Gets or initializes the name of the activity to filter.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets or initializes the versions of the activity to filter.
        /// </summary>
        public IReadOnlyList<string> Versions { get; init; }
    }

    /// <summary>
    /// Specifies an entity filter.
    /// </summary>
    public struct EntityFilter
    {
        /// <summary>
        /// Gets or initializes the name of the entity to filter.
        /// </summary>
        public string Name { get; init; }
    }
}
