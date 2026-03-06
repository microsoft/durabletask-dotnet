// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
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
    /// Gets the currently registered activities.
    /// </summary>
    internal IDictionary<TaskName, Func<IServiceProvider, ITaskActivity>> Activities { get; }
        = new Dictionary<TaskName, Func<IServiceProvider, ITaskActivity>>();

    /// <summary>
    /// Gets the currently registered orchestrators.
    /// </summary>
    internal IDictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>> Orchestrators { get; }
        = new Dictionary<TaskName, Func<IServiceProvider, ITaskOrchestrator>>();

    /// <summary>
    /// Gets the currently registered entities.
    /// </summary>
    internal IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>> Entities { get; }
        = new Dictionary<TaskName, Func<IServiceProvider, ITaskEntity>>();

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
        if (this.Activities.ContainsKey(name))
        {
            throw new ArgumentException($"An {nameof(ITaskActivity)} named '{name}' is already added.", nameof(name));
        }

        this.Activities.Add(name, factory);
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
        if (this.Orchestrators.ContainsKey(name))
        {
            throw new ArgumentException(
                $"An {nameof(ITaskOrchestrator)} named '{name}' is already added.", nameof(name));
        }

        this.Orchestrators.Add(name, _ => factory());
        return this;
    }

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

    /// <summary>
    /// Gets the task name from a delegate by checking for a <see cref="DurableTaskAttribute"/>
    /// or falling back to the method name.
    /// </summary>
    /// <param name="delegate">The delegate to extract the name from.</param>
    /// <returns>The task name.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    static TaskName GetTaskNameFromDelegate(Delegate @delegate)
    {
        MethodInfo method = @delegate.Method;

        // Check for DurableTaskAttribute on the method
        DurableTaskAttribute? attribute = method.GetCustomAttribute<DurableTaskAttribute>();
        string attributeName = attribute?.Name.Name ?? string.Empty;
        if (!string.IsNullOrEmpty(attributeName))
        {
            return new TaskName(attributeName);
        }

        // Fall back to method name
        string? methodName = method.Name;
        if (string.IsNullOrEmpty(methodName) || methodName.StartsWith("<", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Cannot infer task name from the delegate. The delegate must either have a " +
                "[DurableTask] attribute with a name, or be a named method (not a lambda or anonymous delegate).",
                nameof(@delegate));
        }

        return new TaskName(methodName);
    }
}
