// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A class that represents work item filters for a Durable Task Worker. These filters are passed to the backend
/// and only work items matching the filters will be processed by the worker. If no filters are provided,
/// the worker will process all work items. To opt-in to work item filtering, call
/// <see cref="DurableTaskWorkerBuilderExtensions.UseWorkItemFilters(IDurableTaskWorkerBuilder)"/> for the
/// auto-generated filters from the worker's <see cref="DurableTaskRegistry"/>, or
/// <see cref="DurableTaskWorkerBuilderExtensions.UseWorkItemFilters(IDurableTaskWorkerBuilder, DurableTaskWorkerWorkItemFilters)"/>
/// to supply explicit filters.
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
        // worker's configured Version exactly (including the empty/unversioned case). The filter must
        // then advertise that single value for each name the worker can actually serve under that
        // version — and omit names the worker would reject either at the pre-dispatch gate or in the
        // factory. The strict version is captured here so per-name dispatch-capability checks below
        // can decide whether to advertise it.
        string? strictWorkerVersion =
            workerOptions?.Versioning?.MatchStrategy == DurableTaskWorkerOptions.VersionMatchStrategy.Strict
                ? workerOptions.Versioning.Version ?? string.Empty
                : null;
        DurableTaskWorkerOptions.UnversionedFallbackMode orchestratorFallbackMode =
            workerOptions?.Versioning?.OrchestratorUnversionedFallback
                ?? DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit;
        DurableTaskWorkerOptions.UnversionedFallbackMode activityFallbackMode =
            workerOptions?.Versioning?.ActivityUnversionedFallback
                ?? DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit;

        // Orchestration filters group registrations by logical name and emit the concrete distinct
        // version set actually registered (treating null/unversioned as ""). When the factory can
        // resolve unmatched versions via the unversioned registration (unversioned-only names under
        // Implicit, or any name with an unversioned registration under CatchAll), we emit an empty
        // version list — the filter wildcard — so the backend delivers versioned work items the
        // factory can handle. Under StrictExactOnly the factory rejects unmatched versioned requests
        // for every name, so the filter emits the concrete version set instead of widening.
        //
        // Under MatchStrategy.Strict the filter narrows to the single configured worker version, but
        // only for names the worker can actually serve under that version. Names that have neither an
        // exact (name, V) registration nor a fallback-serviceable registration under the configured
        // mode are omitted from the filter entirely so the backend does not stream work items the
        // worker would then reject after the fact.
        //
        // Orchestrator and activity fallback are configured independently, so each filter set
        // consults its own mode.
        List<OrchestrationFilter> orchestrationFilters = registry.OrchestratorsByVersion
            .GroupBy(orchestration => orchestration.Key.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => BuildFilter(
                group.Key,
                group.Select(entry => entry.Key.Version),
                strictWorkerVersion,
                orchestratorFallbackMode,
                static (name, versions) => new OrchestrationFilter { Name = name, Versions = versions }))
            .ToList();

        List<ActivityFilter> activityFilters = registry.ActivitiesByVersion
            .GroupBy(activity => activity.Key.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => BuildFilter(
                group.Key,
                group.Select(entry => entry.Key.Version),
                strictWorkerVersion,
                activityFallbackMode,
                static (name, versions) => new ActivityFilter { Name = name, Versions = versions }))
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

        static IEnumerable<TFilter> BuildFilter<TFilter>(
            string name,
            IEnumerable<string?> registeredVersions,
            string? strictWorkerVersion,
            DurableTaskWorkerOptions.UnversionedFallbackMode mode,
            Func<string, IReadOnlyList<string>, TFilter> create)
        {
            string[] normalized = NormalizeVersions(registeredVersions);

            if (strictWorkerVersion is not null)
            {
                // Strict mode: advertise the single worker version, but only for names the worker can
                // actually serve under it. Omit names that would always be rejected.
                if (CanServeStrictVersion(normalized, strictWorkerVersion, mode))
                {
                    yield return create(name, [strictWorkerVersion]);
                }

                yield break;
            }

            yield return create(name, GetFilterVersions(normalized, mode));
        }

        static string[] NormalizeVersions(IEnumerable<string?> versions)
            => versions
                .Select(version => version ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        static bool CanServeStrictVersion(
            string[] registeredVersions,
            string strictVersion,
            DurableTaskWorkerOptions.UnversionedFallbackMode mode)
        {
            // Exact match always wins, regardless of mode. The empty-version case is covered by the
            // same check: strictVersion == "" only matches an unversioned registration.
            bool hasExact = registeredVersions.Contains(strictVersion, StringComparer.OrdinalIgnoreCase);
            if (hasExact)
            {
                return true;
            }

            // No exact match: only fallback can save us. Replicates DurableTaskFactory's
            // ShouldUseUnversionedFallback logic so the filter agrees with the factory's dispatch
            // decision.
            bool hasUnversioned = registeredVersions.Contains(string.Empty, StringComparer.OrdinalIgnoreCase);
            if (!hasUnversioned)
            {
                return false;
            }

            bool hasAnyVersioned = registeredVersions.Any(v => v.Length > 0);
            return mode switch
            {
                DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll => true,
                DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit => !hasAnyVersioned,
                DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly => false,
                _ => !hasAnyVersioned,
            };
        }

        static IReadOnlyList<string> GetFilterVersions(
            string[] normalized,
            DurableTaskWorkerOptions.UnversionedFallbackMode mode)
        {
            // StrictExactOnly disables every fallback path, including the long-standing implicit
            // unversioned-only fallback. Emit the concrete registered version set so the backend
            // does not deliver versioned work items the factory will reject after the fact.
            if (mode == DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly)
            {
                return normalized;
            }

            // Otherwise, widen to a wildcard when the factory can actually resolve unmatched versions:
            //   - Implicit: only when the registry has no versioned siblings for this name (i.e.
            //     normalized is exactly [""]).
            //   - CatchAll: whenever the registry has an unversioned registration for this name.
            bool hasUnversionedRegistration =
                normalized.Contains(string.Empty, StringComparer.OrdinalIgnoreCase);
            bool implicitWildcard =
                mode == DurableTaskWorkerOptions.UnversionedFallbackMode.Implicit
                && normalized.Length == 1
                && normalized[0].Length == 0;
            bool catchAllWildcard =
                mode == DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll
                && hasUnversionedRegistration;

            if (implicitWildcard || catchAllWildcard)
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
