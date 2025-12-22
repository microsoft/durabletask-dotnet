// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
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

      by func/action (with explicit name):
        Func{Context, Input, Task{Output}}
        Func{Context, Input, Task}
        Func{Context, Input, Output}
        Func{Context, Task{Output}}
        Func{Context, Task}
        Func{Context, Output}
        Action{Context, TInput}
        Action{Context}

      by func/action (name inferred from method or [DurableTask] attribute):
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

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc<TInput, TOutput>(
        Func<TaskActivityContext, TInput, Task<TOutput>> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc<TInput, TOutput>(
        Func<TaskActivityContext, TInput, TOutput> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc<TInput>(Func<TaskActivityContext, TInput, Task> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc<TOutput>(Func<TaskActivityContext, Task<TOutput>> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc(Func<TaskActivityContext, Task> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <typeparam name="TOutput">The activity output type.</typeparam>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc<TOutput>(Func<TaskActivityContext, TOutput> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <typeparam name="TInput">The activity input type.</typeparam>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc<TInput>(Action<TaskActivityContext, TInput> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
    }

    /// <summary>
    /// Registers an activity factory, where the implementation is <paramref name="activity" />.
    /// The name is inferred from a <see cref="DurableTaskAttribute"/> on the method, or the method name.
    /// </summary>
    /// <param name="activity">The activity implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if the name cannot be inferred from the delegate.
    /// </exception>
    public DurableTaskRegistry AddActivityFunc(Action<TaskActivityContext> activity)
    {
        Check.NotNull(activity);
        return this.AddActivityFunc(GetActivityNameFromDelegate(activity), activity);
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
    static TaskName GetActivityNameFromDelegate(Delegate @delegate)
    {
        MethodInfo method = @delegate.Method;

        // Check for DurableTaskAttribute on the method
        DurableTaskAttribute? attribute = method.GetCustomAttribute<DurableTaskAttribute>();
        if (attribute?.Name.Name is not null and not "")
        {
            return attribute.Name;
        }

        // Fall back to method name
        string? methodName = method.Name;
        if (string.IsNullOrEmpty(methodName) || methodName.StartsWith("<", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Cannot infer activity name from the delegate. The delegate must either have a " +
                "[DurableTask] attribute with a name, or be a named method (not a lambda or anonymous delegate).",
                nameof(@delegate));
        }

        return new TaskName(methodName);
    }
}
