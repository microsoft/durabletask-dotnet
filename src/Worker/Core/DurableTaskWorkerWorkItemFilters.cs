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
    /// Initializes a new instance of the <see cref="DurableTaskWorkerWorkItemFilters"/> class.
    /// </summary>
    public DurableTaskWorkerWorkItemFilters()
    {
        this.Orchestrations = [];
        this.Activities = [];
        this.Entities = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskWorkerWorkItemFilters"/> class.
    /// </summary>
    /// <param name="registry"><see cref="DurableTaskRegistry"/> to construct the filter from.</param>
    /// <param name="workerOptions"><see cref="DurableTaskWorkerOptions"/> that optionally provides versioning information.</param>
    internal DurableTaskWorkerWorkItemFilters(DurableTaskRegistry registry, DurableTaskWorkerOptions? workerOptions)
    {
        List<OrchestrationFilter> orchestrationActions = new();
        foreach (var orchestration in registry.Orchestrators)
        {
            orchestrationActions.Add(new OrchestrationFilter
            {
                Name = orchestration.Key,

                // TODO: Support multiple orchestration versions, for now, utilize the Worker's version.
                Versions = workerOptions?.Versioning != null ? [workerOptions.Versioning.DefaultVersion] : [],
            });
        }

        this.Orchestrations = orchestrationActions;
        List<ActivityFilter> activityActions = new();
        foreach (var activity in registry.Activities)
        {
            activityActions.Add(new ActivityFilter
            {
                Name = activity.Key,

                // TODO: Support multiple activity versions, for now, utilize the Worker's version.
                Versions = workerOptions?.Versioning != null ? [workerOptions.Versioning.DefaultVersion] : [],
            });
        }

        this.Activities = activityActions;
        List<EntityFilter> entityActions = new();
        foreach (var entity in registry.Entities)
        {
            entityActions.Add(new EntityFilter
            {
                // Entity names are normalized to lowercase in the backend.
                Name = entity.Key.ToString().ToLowerInvariant(),
            });
        }

        this.Entities = entityActions;
    }

    /// <summary>
    /// Gets or initializes the orchestration filters.
    /// </summary>
    public IReadOnlyList<OrchestrationFilter> Orchestrations { get; init; }

    /// <summary>
    /// Gets or initializes the activity filters.
    /// </summary>
    public IReadOnlyList<ActivityFilter> Activities { get; init; }

    /// <summary>
    /// Gets or initializes the entity filters.
    /// </summary>
    public IReadOnlyList<EntityFilter> Entities { get; init; }

    /// <summary>
    /// Struct specifying an orchestration filter.
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
        public List<string> Versions { get; init; }
    }

    /// <summary>
    /// Struct specifying an activity filter.
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
        public List<string> Versions { get; init; }
    }

    /// <summary>
    /// Struct specifying an entity filter.
    /// </summary>
    public struct EntityFilter
    {
        /// <summary>
        /// Gets or initializes the name of the entity to filter.
        /// </summary>
        public string Name { get; init; }
    }
}
