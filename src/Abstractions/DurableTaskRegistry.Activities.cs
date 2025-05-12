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
    /// Registers an activity factory, resolving the provided type with the service provider.
    /// </summary>
    /// <param name="name">The name of the activity to register.</param>
    /// <param name="type">The activity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(TaskName name, Type type)
    {
        Check.ConcreteType<ITaskActivity>(type);
        return this.AddActivity(name, sp => (ITaskActivity)ActivatorUtilities.GetServiceOrCreateInstance(sp, type));
    }

    /// <summary>
    /// Registers an activity factory, resolving the provided type with the service provider. The TaskName used is
    /// derived from the provided type information.
    /// </summary>
    /// <param name="type">The activity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(Type type)
        => this.AddActivity(type.GetTaskName(), type);

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
    /// <param name="activity">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(TaskName name, ITaskActivity activity)
    {
        Check.NotNull(activity);
        return this.AddActivity(name, (IServiceProvider _) => activity);
    }

    /// <summary>
    /// Registers an activity singleton.
    /// </summary>
    /// <param name="activity">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddActivity(ITaskActivity activity)
    {
        Check.NotNull(activity);
        return this.AddActivity(activity.GetType().GetTaskName(), activity);
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
    {
        Check.NotNull(activity);
        ITaskActivity wrapper = FuncTaskActivity.Create(activity);
        return this.AddActivity(name, wrapper);
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
