// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Validates that every name configured on <see cref="DurableTaskWorkerWorkItemFilters"/> for a
/// specific named worker matches a task registered with that worker's <see cref="DurableTaskRegistry"/>.
/// </summary>
/// <remarks>
/// Registered through <see cref="DurableTaskWorkerBuilderExtensions.UseWorkItemFilters(IDurableTaskWorkerBuilder, DurableTaskWorkerWorkItemFilters?)"/>
/// when the caller provides explicit filters. The validator runs lazily when
/// <see cref="IOptionsMonitor{TOptions}.Get(string?)"/> is first called for the matching worker name
/// (effectively at worker construction), so callers do not need to invoke validation explicitly.
/// </remarks>
sealed class DurableTaskWorkerWorkItemFiltersValidator : IValidateOptions<DurableTaskWorkerWorkItemFilters>
{
    readonly string builderName;
    readonly IOptionsMonitor<DurableTaskRegistry> registryMonitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskWorkerWorkItemFiltersValidator"/> class.
    /// </summary>
    /// <param name="builderName">The named-options name of the worker whose filters this validator is bound to.</param>
    /// <param name="registryMonitor">The monitor used to resolve the worker's <see cref="DurableTaskRegistry"/> at validation time.</param>
    public DurableTaskWorkerWorkItemFiltersValidator(
        string builderName, IOptionsMonitor<DurableTaskRegistry> registryMonitor)
    {
        this.builderName = builderName;
        this.registryMonitor = Check.NotNull(registryMonitor);
    }

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, DurableTaskWorkerWorkItemFilters options)
    {
        // Only validate the named options instance for the worker this validator was registered against.
        if (!string.Equals(name, this.builderName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        Check.NotNull(options);

        DurableTaskRegistry registry = this.registryMonitor.Get(this.builderName);

        List<string> unknownOrchestrations = FindUnknown(
            options.Orchestrations.Select(o => o.Name), n => registry.Orchestrators.ContainsKey(n));
        List<string> unknownActivities = FindUnknown(
            options.Activities.Select(a => a.Name), n => registry.Activities.ContainsKey(n));
        List<string> unknownEntities = FindUnknown(
            options.Entities.Select(e => e.Name), n => registry.Entities.ContainsKey(n));

        if (unknownOrchestrations.Count == 0
            && unknownActivities.Count == 0
            && unknownEntities.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        StringBuilder sb = new();
        sb.Append("Cannot configure work item filters for worker '").Append(this.builderName)
          .Append("': the following filter names do not match any registered task. ")
          .Append("Register them on the worker (via AddTasks/AddOrchestrator/AddActivity/AddEntity) ")
          .Append("or remove them from the filters.");
        AppendCategory(sb, "Orchestrations", unknownOrchestrations);
        AppendCategory(sb, "Activities", unknownActivities);
        AppendCategory(sb, "Entities", unknownEntities);

        return ValidateOptionsResult.Fail(sb.ToString());
    }

    static List<string> FindUnknown(IEnumerable<string> names, Func<TaskName, bool> isRegistered)
    {
        List<string> unknown = [];
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                unknown.Add("<empty>");
                continue;
            }

            // TaskName equality is OrdinalIgnoreCase, mirroring how registered keys are compared.
            // Construct the TaskName explicitly so the conversion is not dependent on the implicit
            // string -> TaskName operator (which could be removed/changed independently).
            if (!isRegistered(new TaskName(name)))
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
