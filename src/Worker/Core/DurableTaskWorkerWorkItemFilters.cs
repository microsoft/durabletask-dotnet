// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A class that represents work item filters for a Durable Task Worker. These filters are passed to the backend
/// and only work items matching the filters will be processed by the worker. If no filters are provided,
/// the worker will process all work items. To opt-in to work item filtering, call
/// <see cref="DurableTaskWorkerBuilderExtensions.UseWorkItemFilters"/> on the worker builder with either
/// explicit filters or auto-generated filters from the <see cref="DurableTaskRegistry"/>.
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
        // Under MatchStrategy.Strict the worker accepts only instances whose version matches the
        // worker's configured Version exactly — including the empty/unversioned case. The filter must
        // narrow each name's version list to that single value (treating null as empty) so the backend
        // does not stream work items the worker will then reject after the fact.
        IReadOnlyList<string>? strictWorkerVersions =
            workerOptions?.Versioning?.MatchStrategy == DurableTaskWorkerOptions.VersionMatchStrategy.Strict
                ? [workerOptions.Versioning.Version ?? string.Empty]
                : null;
        bool useUnversionedFallback =
            workerOptions?.Versioning?.UnversionedFallback == DurableTaskWorkerOptions.UnversionedFallbackMode.WhenNoExactMatch;

        // Orchestration filters group registrations by logical name and emit the concrete distinct
        // version set actually registered (treating null/unversioned as ""). Strict mode overrides
        // this with the single configured worker version. When the factory can resolve unknown
        // versions via an unversioned registration (unversioned-only names, or mixed names with
        // opt-in unversioned fallback), we emit an empty version list — the filter wildcard — so the
        // backend can deliver versioned work items the factory can handle. Otherwise, emitting the
        // concrete version set prevents the backend from streaming work items the worker would then
        // reject after the fact.
        List<OrchestrationFilter> orchestrationFilters = registry.OrchestratorsByVersion
            .GroupBy(orchestration => orchestration.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                IReadOnlyList<string> versions =
                    strictWorkerVersions ?? GetFilterVersions(group.Select(entry => entry.Key.Version), useUnversionedFallback);

                return new OrchestrationFilter
                {
                    Name = group.Key,
                    Versions = versions,
                };
            })
            .ToList();

        List<ActivityFilter> activityFilters = registry.ActivitiesByVersion
            .GroupBy(activity => activity.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                IReadOnlyList<string> versions =
                    strictWorkerVersions ?? GetFilterVersions(group.Select(entry => entry.Key.Version), useUnversionedFallback);

                return new ActivityFilter
                {
                    Name = group.Key,
                    Versions = versions,
                };
            })
            .ToList();

        return new DurableTaskWorkerWorkItemFilters
        {
            Orchestrations = orchestrationFilters,
            Activities = activityFilters,
            Entities = registry.Entities.Select(entity => new EntityFilter
            {
                // Entity names are normalized to lowercase in the backend.
                Name = entity.Key.ToString(),
            }).ToList(),
        };

        static IReadOnlyList<string> GetFilterVersions(IEnumerable<string?> versions, bool useUnversionedFallback)
        {
            // Normalize null to "" so an unversioned registration appears consistently.
            string[] normalized = versions
                .Select(version => version ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Unversioned-only: emit the wildcard match-all (empty list) so the backend can deliver
            // versioned work items that the factory will resolve via unversioned fallback. Without
            // this, callers asking for a specific version would be filtered out at the backend even
            // though the worker can handle them.
            if ((normalized.Length == 1 && normalized[0].Length == 0)
                || (useUnversionedFallback && normalized.Contains(string.Empty, StringComparer.OrdinalIgnoreCase)))
            {
                return [];
            }

            return normalized;
        }
    }

    /// <summary>
    /// Specifies an orchestration filter.
    /// </summary>
    /// <param name="name">The name of the orchestration.</param>
    /// <param name="versions">The optional versions of the orchestration.</param>
    public readonly struct OrchestrationFilter(string name, IReadOnlyList<string>? versions)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OrchestrationFilter"/> struct with default values.
        /// </summary>
        public OrchestrationFilter()
            : this(string.Empty, [])
        {
        }

        /// <summary>
        /// Gets or initializes the name of the orchestration to filter.
        /// </summary>
        public string Name { get; init; } = name;

        /// <summary>
        /// Gets or initializes the versions of the orchestration to filter.
        /// </summary>
        public IReadOnlyList<string> Versions { get; init; } = versions ?? [];
    }

    /// <summary>
    /// Specifies an activity filter.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="versions">The optional versions of the activity.</param>
    public readonly struct ActivityFilter(string name, IReadOnlyList<string>? versions)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityFilter"/> struct with default values.
        /// </summary>
        public ActivityFilter()
            : this(string.Empty, [])
        {
        }

        /// <summary>
        /// Gets or initializes the name of the activity to filter.
        /// </summary>
        public string Name { get; init; } = name;

        /// <summary>
        /// Gets or initializes the versions of the activity to filter.
        /// </summary>
        public IReadOnlyList<string> Versions { get; init; } = versions ?? [];
    }

    /// <summary>
    /// Specifies an entity filter.
    /// </summary>
    /// <param name="name">The name of the entity.</param>
    public readonly struct EntityFilter(string name)
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EntityFilter"/> struct with default values.
        /// </summary>
        public EntityFilter()
            : this(string.Empty)
        {
        }

        /// <summary>
        /// Gets or initializes the name of the entity to filter.
        /// </summary>
        public string Name { get; init; } = name;
    }
}
