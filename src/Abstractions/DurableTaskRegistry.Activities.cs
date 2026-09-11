// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask;

/// <summary>
/// Container for registered <see cref="ITaskOrchestrator" />, <see cref="ITaskActivity" />,
/// and <see cref="Microsoft.DurableTask.Entities.ITaskEntity"/> implementations.
/// </summary>
public sealed partial class DurableTaskRegistry
{
    /*
      Covers the following ways to add activities.
      by type:
        Type argument
        TaskName and Type argument
        TActivity generic parameter
        TaskName and TActivity generic parameter
        ITaskActivity singleton
        TaskName ITaskActivity singleton

      by func/action:
        Func{Context, Input, Task{Output}}
        Func{Context, Input, Task}
        Func{Context, Input, Output}
        Func{Context, Task{Output}}
        Func{Context, Task}
        Func{Context, Output}
        Action{Context, TInput}
        Action{Context}
    */

    /// <summary>
    /// Registers an activity factory.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="version">The activity version.</param>
    /// <param name="factory">The activity factory.</param>
    /// <returns>This registry instance, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>If <paramref name="name"/> is <c>default</c>.</item>
    ///   <item>If <paramref name="name" /> and <paramref name="version" /> are already registered.</item>
    ///   <item>If <paramref name="factory"/> is <c>null</c>.</item>
    /// </list>
    /// </exception>
    public DurableTaskRegistry AddActivity(TaskName name, TaskVersion version, Func<ITaskActivity> factory)
    {
        Check.NotNull(factory);
        return this.AddActivity(name, version, _ => factory());
    }

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider.
    /// </summary>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="type">The activity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(TaskName name, Type type)
    {
        Check.ConcreteType<ITaskActivity>(type);
        return this.AddActivityAllVersions(
            name,
            type.GetDurableTaskVersions(),
            sp => (ITaskActivity)ActivatorUtilities.GetServiceOrCreateInstance(sp, type));
    }

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider. The TaskName used is
    /// derived from the provided type information.
    /// </summary>
    /// <param name="type">The activity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(Type type)
    {
        Check.ConcreteType<ITaskActivity>(type);
        return this.AddActivityAllVersions(
            type.GetTaskName(),
            type.GetDurableTaskVersions(),
            sp => (ITaskActivity)ActivatorUtilities.GetServiceOrCreateInstance(sp, type));
    }

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider.
    /// </summary>
    /// <typeparam name="TActivity">The type of activity to register.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity<TActivity>(TaskName name)
        where TActivity : class, ITaskActivity
        => this.AddActivity(name, typeof(TActivity));

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider. The TaskName used is
    /// derived from the provided type information.
    /// </summary>
    /// <typeparam name="TActivity">The type of activity to register.</typeparam>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity<TActivity>()
        where TActivity : class, ITaskActivity
        => this.AddActivity(typeof(TActivity));

    /// <summary>
    /// Registers an activity singleton.
    /// </summary>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(TaskName name, ITaskActivity activity)
    {
        Check.NotNull(activity);
        return this.AddActivityAllVersions(name, activity.GetType().GetDurableTaskVersions(), _ => activity);
    }

    /// <summary>
    /// Registers an activity singleton.
    /// </summary>
    /// <param name="activity">The activity instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(ITaskActivity activity)
    {
        Check.NotNull(activity);
        return this.AddActivityAllVersions(
            activity.GetType().GetTaskName(),
            activity.GetType().GetDurableTaskVersions(),
            _ => activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TInput, TOutput>(
        TaskName name, Func<TaskActivityContext, TInput, Task<TOutput>> activity)
        => this.AddActivityFunc(name, default, activity);

    /// <summary>
    /// Registers an activity factory at the specified version, where the implementation is the supplied
    /// lambda. This is the canonical version-aware lambda overload that the other
    /// <c>AddActivityFunc</c> overloads pass through to with an unversioned default.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="version">
    /// The version of the activity. Pass <see cref="TaskVersion.Unversioned"/> (or
    /// <c>default(TaskVersion)</c>) to register an unversioned activity.
    /// </param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TInput, TOutput>(
        TaskName name, TaskVersion version, Func<TaskActivityContext, TInput, Task<TOutput>> activity)
    {
        Check.NotNull(activity);
        ITaskActivity wrapper = FuncTaskActivity.Create(activity);
        return this.AddActivity(name, version, () => wrapper);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TInput, TOutput>(
        TaskName name, Func<TaskActivityContext, TInput, TOutput> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<TInput, TOutput>(
            name, (context, input) => Task.FromResult(activity.Invoke(context, input)));
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TInput>(TaskName name, Func<TaskActivityContext, TInput, Task> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<TInput, object?>(name, async (context, input) =>
        {
            await activity(context, input);
            return null;
        });
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TOutput>(TaskName name, Func<TaskActivityContext, Task<TOutput>> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<object?, TOutput>(name, (context, _) => activity(context));
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc(TaskName name, Func<TaskActivityContext, Task> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<object?, object?>(name, async (context, _) =>
        {
            await activity(context);
            return null;
        });
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TOutput>(TaskName name, Func<TaskActivityContext, TOutput> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<object?, TOutput>(name, (context, _) => activity(context));
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc<TInput>(TaskName name, Action<TaskActivityContext, TInput> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<TInput, object?>(name, (context, input) =>
        {
            activity(context, input);
            return CompletedNullTask;
        });
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// </summary>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivityFunc(TaskName name, Action<TaskActivityContext> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc<object?, object?>(name, (context, input) =>
        {
            activity(context);
            return CompletedNullTask;
        });
    }
}
