// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Shims;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for <see cref="DurableTaskRegistry" />.
/// </summary>
public static partial class DurableTaskRegistryExtensions
{
    static readonly Task<object?> CompletedNullTask = Task.FromResult<object?>(null);

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="type">The activity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity(this DurableTaskRegistry registry, TaskName name, Type type)
    {
        Check.NotNull(registry);
        Check.ConcreteType<ITaskActivity>(type);
        return registry.AddActivity(name, sp => (ITaskActivity)ActivatorUtilities.GetServiceOrCreateInstance(sp, type));
    }

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider. The TaskName used is
    /// derived from the provided type information.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="type">The activity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity(this DurableTaskRegistry registry, Type type)
        => registry.AddActivity(type.GetTaskName(), type);

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider.
    /// </summary>
    /// <typeparam name="TActivity">The type of activity to register.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TActivity>(this DurableTaskRegistry registry, TaskName name)
        where TActivity : class, ITaskActivity
        => registry.AddActivity(name, typeof(TActivity));

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider. The TaskName used is
    /// derived from the provided type information.
    /// </summary>
    /// <typeparam name="TActivity">The type of activity to register.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TActivity>(this DurableTaskRegistry registry)
        where TActivity : class, ITaskActivity
        => registry.AddActivity(typeof(TActivity));

    /// <summary>
    /// Registers an activity singleton.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity(
        this DurableTaskRegistry registry, TaskName name, ITaskActivity activity)
    {
        Check.NotNull(registry);
        Check.NotNull(activity);
        return registry.AddActivity(name, _ => activity);
    }

    /// <summary>
    /// Registers an activity singleton.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="activity">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity(this DurableTaskRegistry registry, ITaskActivity activity)
    {
        Check.NotNull(registry);
        Check.NotNull(activity);
        return registry.AddActivity(activity.GetType().GetTaskName(), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TInput, TOutput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskActivityContext, TInput, Task<TOutput>> activity)
    {
        Check.NotNull(registry);
        Check.NotNull(activity);
        ITaskActivity wrapper = FuncTaskActivity.Create(activity!); // TODO: remove '!'
        return registry.AddActivity(name, wrapper);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TInput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskActivityContext, TInput, Task> activity)
    {
        Check.NotNull(registry);
        Check.NotNull(activity);
        return registry.AddActivity<TInput, object?>(name, async (context, input) =>
        {
            await activity(context, input);
            return null;
        });
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TInput, TOutput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskActivityContext, TInput, TOutput> activity)
    {
        Check.NotNull(registry);
        Check.NotNull(activity);
        return registry.AddActivity<TInput, TOutput>(
            name, (context, input) => Task.FromResult(activity.Invoke(context, input)));
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TOutput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskActivityContext, Task<TOutput>> activity)
    {
        Check.NotNull(activity);
        return registry.AddActivity<object?, TOutput>(name, (context, _) => activity(context));
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity<TInput>(
        this DurableTaskRegistry registry, TaskName name, Action<TaskActivityContext, TInput> activity)
    {
        Check.NotNull(activity);
        return registry.AddActivity<TInput, object?>(name, (context, input) =>
        {
            activity(context, input);
            return CompletedNullTask;
        });
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddActivity(
        this DurableTaskRegistry registry, TaskName name, Action<TaskActivityContext> activity)
    {
        Check.NotNull(activity);
        return registry.AddActivity<object?, object?>(name, (context, input) =>
        {
            activity(context);
            return CompletedNullTask;
        });
    }
}
