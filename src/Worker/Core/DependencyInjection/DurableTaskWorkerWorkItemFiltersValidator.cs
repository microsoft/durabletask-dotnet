// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Validates that every name configured on <see cref="DurableTaskWorkerWorkItemFilters"/> matches a
/// task registered with the corresponding named worker's <see cref="DurableTaskRegistry"/>.
/// </summary>
/// <remarks>
/// Registered as a single global <see cref="IValidateOptions{TOptions}"/> via
/// <see cref="DurableTaskWorkerBuilderExtensions.UseWorkItemFilters(IDurableTaskWorkerBuilder, DurableTaskWorkerWorkItemFilters?)"/>.
/// The Options framework dispatches the named-options name to <see cref="Validate(string?, DurableTaskWorkerWorkItemFilters)"/>,
/// which is then used to resolve the matching <see cref="DurableTaskRegistry"/>. Validation runs
/// lazily when <see cref="IOptionsMonitor{TOptions}.Get(string?)"/> is first called for a worker
/// (effectively at worker construction), so callers do not need to invoke validation explicitly.
/// </remarks>
sealed class DurableTaskWorkerWorkItemFiltersValidator : IValidateOptions<DurableTaskWorkerWorkItemFilters>
{
    readonly IOptionsMonitor<DurableTaskRegistry> registryMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskWorkerWorkItemFiltersValidator"/> class.
    /// </summary>
    /// <param name="registryMonitor">The monitor used to resolve the worker's <see cref="DurableTaskRegistry"/> at validation time.</param>
    public DurableTaskWorkerWorkItemFiltersValidator(IOptionsMonitor<DurableTaskRegistry> registryMonitor)
    {
        this.registryMonitor = Check.NotNull(registryMonitor);
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, DurableTaskWorkerWorkItemFilters options)
    {
        Check.NotNull(options);

        // The validator is registered globally, so the Options framework dispatches every named
        // worker's filter options through it -- including workers that never opted into filtering
        // and therefore have no filter entries to validate. Skip those cases so the validator only
        // reports a verdict for workers that actually configured filters.
        if (options.Orchestrations.Count == 0
            && options.Activities.Count == 0
            && options.ExcludedActivities.Count == 0
            && options.Entities.Count == 0)
        {
            return ValidateOptionsResult.Skip;
        }

        DurableTaskRegistry registry = this.registryMonitor.Get(name);

        HashSet<string> registeredOrchestratorNames = new(
            registry.OrchestratorsByVersion.Keys.Select(k => k.Name),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> registeredActivityNames = new(
            registry.ActivitiesByVersion.Keys.Select(k => k.Name),
            StringComparer.OrdinalIgnoreCase);

        List<string> unknownOrchestrations = FindUnknown(
            options.Orchestrations.Select(o => o.Name), registeredOrchestratorNames.Contains);
        List<string> unknownActivities = FindUnknown(
            options.Activities.Select(a => a.Name), registeredActivityNames.Contains);
        List<string> unknownExcludedActivities = FindUnknown(
            options.ExcludedActivities.Select(a => a.Name), registeredActivityNames.Contains);
        List<string> unknownEntities = FindUnknown(
            options.Entities.Select(e => e.Name), n => registry.Entities.ContainsKey(new TaskName(n)));

        if (unknownOrchestrations.Count == 0
            && unknownActivities.Count == 0
            && unknownExcludedActivities.Count == 0
            && unknownEntities.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        StringBuilder sb = new();
        string displayName = string.IsNullOrEmpty(name) ? "<default>" : name!;
        sb.Append("Cannot configure work item filters for worker '").Append(displayName)
          .Append("': the following filter names do not match any registered task. ")
          .Append("Register them on the worker (via AddTasks/AddOrchestrator/AddActivity/AddEntity) ")
          .Append("or remove them from the filters.");
        AppendCategory(sb, "Orchestrations", unknownOrchestrations);
        AppendCategory(sb, "Activities", unknownActivities);
        AppendCategory(sb, "ExcludedActivities", unknownExcludedActivities);
        AppendCategory(sb, "Entities", unknownEntities);

        return ValidateOptionsResult.Fail(sb.ToString());
    }

    static List<string> FindUnknown(IEnumerable<string> names, Func<string, bool> isRegistered)
    {
        List<string> unknown = [];
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                unknown.Add("<empty>");
                continue;
            }

            if (!isRegistered(name))
            {
                unknown.Add(name);
            }
        }

        return unknown;
    }

    static void AppendCategory(StringBuilder sb, string category, List<string> unknown)
    {
        if (unknown.Count == 0)
        {
            return;
        }

        sb.Append(' ').Append(category).Append(": [").Append(string.Join(", ", unknown)).Append(']');
    }
}
