// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask;

/// <summary>
/// Container for registered <see cref="ITaskOrchestrator" />, <see cref="ITaskActivity" />,
/// and <see cref="ITaskEntity"/> implementations.
/// </summary>
public sealed partial class DurableTaskRegistry
{
    static readonly Task<object?> CompletedNullTask = Task.FromResult<object?>(null);

    /// <summary>
    /// Gets the currently registered activities, keyed by <see cref="TaskName"/> and <see cref="TaskVersion"/>.
    /// </summary>
    internal IDictionary<TaskVersionKey, Func<IServiceProvider, ITaskActivity>> ActivitiesByVersion { get; }
        = new Dictionary<TaskVersionKey, Func<IServiceProvider, ITaskActivity>>();

    /// <summary>
    /// Gets the currently registered orchestrators, keyed by <see cref="TaskName"/> and <see cref="TaskVersion"/>.
    /// </summary>
    internal IDictionary<TaskVersionKey, Func<IServiceProvider, ITaskOrchestrator>> OrchestratorsByVersion { get; }
        = new Dictionary<TaskVersionKey, Func<IServiceProvider, ITaskOrchestrator>>();

    /// <summary>
    /// Gets the currently registered entities.
    /// </summary>
    internal IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> Entities { get; }
        = new Dictionary<TaskName, Func<IServiceProvider, ITaskEntity>>();

    /// <summary>
    /// Gets the currently registered orchestrators as a name-keyed enumeration. One entry per
    /// registration; multi-version registrations appear as multiple entries sharing the same
    /// <see cref="TaskName"/>.
    /// </summary>
    /// <remarks>
    /// This shape preserves binary compatibility with the currently shipped
    /// <c>Microsoft.Azure.Functions.Worker.Extensions.DurableTask</c> 1.4.0, which reflects on this
    /// property and casts it to
    /// <see cref="IEnumerable{T}"/> of
    /// <see cref="KeyValuePair{TKey, TValue}"/> of <see cref="TaskName"/> and
    /// <c>Func&lt;IServiceProvider, ITaskOrchestrator&gt;</c>. Internal SDK code should use
    /// <see cref="OrchestratorsByVersion"/>; external code should use <see cref="GetOrchestrators"/>.
    /// </remarks>
    internal IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> Orchestrators
        => this.OrchestratorsByVersion.Select(kvp =>
            new KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>(kvp.Key.Name, kvp.Value));

    /// <summary>
    /// Gets the currently registered activities as a name-keyed enumeration. One entry per
    /// registration; multi-version registrations appear as multiple entries sharing the same
    /// <see cref="TaskName"/>.
    /// </summary>
    /// <remarks>
    /// This shape preserves binary compatibility with the currently shipped
    /// <c>Microsoft.Azure.Functions.Worker.Extensions.DurableTask</c> 1.4.0, which reflects on this
    /// property and casts it to
    /// <see cref="IEnumerable{T}"/> of
    /// <see cref="KeyValuePair{TKey, TValue}"/> of <see cref="TaskName"/> and
    /// <c>Func&lt;IServiceProvider, ITaskActivity&gt;</c>. Internal SDK code should use
    /// <see cref="ActivitiesByVersion"/>; external code should use <see cref="GetActivities"/>.
    /// </remarks>
    internal IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> Activities
        => this.ActivitiesByVersion.Select(kvp =>
            new KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>(kvp.Key.Name, kvp.Value));

    /// <summary>
    /// Enumerates the registered orchestrators. One entry per registration; multi-version
    /// registrations appear as multiple entries sharing the same <see cref="TaskName"/>.
    /// </summary>
    /// <returns>
    /// An enumeration of <see cref="KeyValuePair{TKey, TValue}"/> pairs where the key is the
    /// orchestrator <see cref="TaskName"/> and the value is the factory delegate.
    /// </returns>
    public IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> GetOrchestrators()
        => this.Orchestrators;

    /// <summary>
    /// Enumerates the registered activities. One entry per registration; multi-version
    /// registrations appear as multiple entries sharing the same <see cref="TaskName"/>.
    /// </summary>
    /// <returns>
    /// An enumeration of <see cref="KeyValuePair{TKey, TValue}"/> pairs where the key is the
    /// activity <see cref="TaskName"/> and the value is the factory delegate.
    /// </returns>
    public IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> GetActivities()
        => this.Activities;

    /// <summary>
    /// Enumerates the registered entities.
    /// </summary>
    /// <returns>
    /// An enumeration of <see cref="KeyValuePair{TKey, TValue}"/> pairs where the key is the
    /// entity <see cref="TaskName"/> and the value is the factory delegate.
    /// </returns>
    public IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskEntity>>> GetEntities()
        => this.Entities.Select(kvp => kvp);

    /// <summary>
    /// Registers an activity factory.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="factory">The activity factory.</param>
    /// <returns>This registry instance, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>If <paramref name="name"/> is <c>default</c>.</item>
    ///   <item>If <paramref name="name" /> is already registered.</item>
    ///   <item>If <paramref name="factory"/> is <c>null</c>.</item>
    /// </list>
    /// </exception>
    public DurableTaskRegistry AddActivity(TaskName name, Func<IServiceProvider, ITaskActivity> factory)
        => this.AddActivity(name, default, factory);

    /// <summary>
    /// Registers an entity factory.
    /// </summary>
    /// <param name="name">The name of the entity.</param>
    /// <param name="factory">The entity factory.</param>
    /// <returns>This registry instance, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>If <paramref name="name"/> is <c>default</c>.</item>
    ///   <item>If <paramref name="name" /> is already registered.</item>
    ///   <item>If <paramref name="factory"/> is <c>null</c>.</item>
    /// </list>
    /// </exception>
    public DurableTaskRegistry AddEntity(TaskName name, Func<IServiceProvider, ITaskEntity> factory)
    {
        Check.NotDefault(name);
        Check.NotNull(factory);
        if (this.Entities.ContainsKey(name))
        {
            throw new ArgumentException($"An {nameof(ITaskEntity)} named '{name}' is already added.", nameof(name));
        }

        this.Entities.Add(name, factory);
        return this;
    }

    DurableTaskRegistry AddActivity(TaskName name, TaskVersion version, Func<IServiceProvider, ITaskActivity> factory)
    {
        Check.NotDefault(name);
        Check.NotNull(factory);

        TaskVersionKey key = new(name, version);
        if (this.ActivitiesByVersion.ContainsKey(key))
        {
            string message = string.IsNullOrEmpty(version.Version)
                ? $"An {nameof(ITaskActivity)} named '{name}' is already added."
                : $"An {nameof(ITaskActivity)} named '{name}' with version '{version.Version}' is already added.";
            throw new ArgumentException(message, nameof(name));
        }

        this.ActivitiesByVersion.Add(key, factory);
        return this;
    }
}
