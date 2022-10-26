// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for the Durable Task worker.
/// </summary>
public sealed partial class DurableTaskRegistry
{
    static readonly Task<object?> CompletedNullTask = Task.FromResult<object?>(null);

    readonly ImmutableDictionary<TaskName, Func<IServiceProvider, ITaskActivity>>.Builder activitiesBuilder
        = ImmutableDictionary.CreateBuilder<TaskName, Func<IServiceProvider, ITaskActivity>>();

    readonly ImmutableDictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>>.Builder orchestratorsBuilder
        = ImmutableDictionary.CreateBuilder<TaskName, Func<IServiceProvider, ITaskOrchestrator>>();

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
    {
        Check.NotDefault(name);
        Check.NotNull(factory);
        if (this.activitiesBuilder.ContainsKey(name))
        {
            throw new ArgumentException($"An {nameof(ITaskActivity)} named '{name}' is already added.", nameof(name));
        }

        this.activitiesBuilder.Add(name, factory);
        return this;
    }

    /// <summary>
    /// Registers an orchestrator factory.
    /// </summary>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="factory">The orchestrator factory.</param>
    /// <returns>This registry instance, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>If <paramref name="name"/> is <c>default</c>.</item>
    ///   <item>If <paramref name="name" /> is already registered.</item>
    ///   <item>If <paramref name="factory"/> is <c>null</c>.</item>
    /// </list>
    /// </exception>
    public DurableTaskRegistry AddOrchestrator(TaskName name, Func<ITaskOrchestrator> factory)
    {
        Check.NotDefault(name);
        Check.NotNull(factory);
        if (this.activitiesBuilder.ContainsKey(name))
        {
            throw new ArgumentException(
                $"An {nameof(ITaskOrchestrator)} named '{name}' is already added.", nameof(name));
        }

        this.orchestratorsBuilder.Add(name, _ => factory());
        return this;
    }

    /// <summary>
    /// Builds this registry into a <see cref="IDurableTaskFactory" />.
    /// </summary>
    /// <returns>The built <see cref="IDurableTaskFactory" />.</returns>
    internal IDurableTaskFactory Build()
    {
        return new DurableTaskFactory(this.activitiesBuilder.ToImmutable(), this.orchestratorsBuilder.ToImmutable());
    }
}
