// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.DurableTask.Shims;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for the Durable Task worker.
/// </summary>
public sealed class DurableTaskRegistry
{
    readonly ImmutableDictionary<TaskName, Func<IServiceProvider, ITaskActivity>>.Builder activitiesBuilder
        = ImmutableDictionary.CreateBuilder<TaskName, Func<IServiceProvider, ITaskActivity>>();

    readonly ImmutableDictionary<TaskName, Func<ITaskOrchestrator>>.Builder orchestratorsBuilder
        = ImmutableDictionary.CreateBuilder<TaskName, Func<ITaskOrchestrator>>();

    /// <inheritdoc/>
    public DurableTaskRegistry AddActivity(TaskName name, Action<TaskActivityContext> implementation)
    {
        return this.AddActivity<object?, object?>(name, (context, _) =>
        {
            implementation(context);
            return null!;
        });
    }

    /// <summary>
    /// Registers an activity as a synchronous (blocking) lambda function that doesn't take any input nor returns any output.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="implementation">The lambda function to invoke when the activity is called.</param>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public DurableTaskRegistry AddActivity(TaskName name, Func<TaskActivityContext, Task> implementation)
    {
        return this.AddActivity<object, object?>(name, (context, input) => implementation(context));
    }

    /// <summary>
    /// Registers an activity as an asynchronous (non-blocking) lambda function.
    /// </summary>
    /// <inheritdoc cref="AddActivity(TaskName, Action{TaskActivityContext})"/>
    public DurableTaskRegistry AddActivity<TInput, TOutput>(
        TaskName name,
        Func<TaskActivityContext, TInput?, TOutput?> implementation)
    {
        return this.AddActivity<TInput, TOutput?>(name, (context, input) => Task.FromResult(implementation(context, input)));
    }

    /// <summary>
    /// Registers an activity as an synchronous (blocking) lambda function with an input and an output.
    /// </summary>
    /// <typeparam name="TInput">The input type of the activity.</typeparam>
    /// <typeparam name="TOutput">The output type of the activity.</typeparam>
    /// <inheritdoc cref="AddActivity(TaskName, Action{TaskActivityContext})"/>
    public DurableTaskRegistry AddActivity<TInput, TOutput>(
        TaskName name,
        Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
    {
        if (name == default)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (implementation == null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        if (this.activitiesBuilder.ContainsKey(name))
        {
            throw new ArgumentException($"A task activity named '{name}' is already added.", nameof(name));
        }

        this.activitiesBuilder.Add(
            name,
            _ => FuncTaskActivity.Create(implementation));
        return this;
    }

    /// <summary>
    /// Registers an activity as an asynchronous (non-blocking) lambda function.
    /// </summary>
    /// <inheritdoc cref="AddActivity{TInput, TOutput}(TaskName, Func{TaskActivityContext, TInput, TOutput})"/>
    public DurableTaskRegistry AddActivity<TActivity>()
        where TActivity : ITaskActivity
    {
        string name = GetTaskName(typeof(TActivity));
        this.activitiesBuilder.Add(
            name,
            sp =>
            {
                return ActivatorUtilities.GetServiceOrCreateInstance<TActivity>(sp);
            });
        return this;
    }

    /// <summary>
    /// Registers an orchestrator function that doesn't take any input nor returns any output.
    /// </summary>
    /// <inheritdoc cref="AddOrchestrator{TInput, TOutput}"/>
    public DurableTaskRegistry AddOrchestrator(
        TaskName name,
        Func<TaskOrchestrationContext, Task> implementation)
    {
        return this.AddOrchestrator<object?, object?>(name, async (ctx, _) =>
        {
            await implementation(ctx);
            return null;
        });
    }

    /// <summary>
    /// Registers an orchestrator function that returns an output but doesn't take any input.
    /// </summary>
    /// <inheritdoc cref="AddOrchestrator{TInput, TOutput}"/>
    public DurableTaskRegistry AddOrchestrator<TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, Task<TOutput?>> implementation)
    {
        return this.AddOrchestrator<object?, TOutput>(name, (ctx, _) => implementation(ctx));
    }

    /// <summary>
    /// Registers an orchestrator function that takes an input and returns an output.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator function's input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator function's return type.</typeparam>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="implementation">The orchestration implementation as a function.</param>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public DurableTaskRegistry AddOrchestrator<TInput, TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation)
    {
        if (name == default)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (implementation == null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        if (this.orchestratorsBuilder.ContainsKey(name))
        {
            throw new ArgumentException($"A task orchestrator named '{name}' is already added.", nameof(name));
        }

        this.orchestratorsBuilder.Add(
            name,
            () => FuncTaskOrchestrator.Create(implementation));

        return this;
    }

    /// <summary>
    /// Registers an orchestrator that implements the <see cref="ITaskOrchestrator"/> interface.
    /// </summary>
    /// <typeparam name="TOrchestrator">The concrete type of the orchestrator.</typeparam>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public DurableTaskRegistry AddOrchestrator<TOrchestrator>()
        where TOrchestrator : ITaskOrchestrator
    {
        string name = GetTaskName(typeof(TOrchestrator));
        this.orchestratorsBuilder.Add(
            name,
            () =>
            {
                // Unlike activities, we don't give orchestrators access to the IServiceProvider collection since
                // injected services are inherently non-deterministic. If an orchestrator needs access to a service,
                // it should invoke that service through an activity call.
                return Activator.CreateInstance<TOrchestrator>();
            });

        return this;
    }

    /// <summary>
    /// Builds this registry into a <see cref="DurableTaskFactory" />.
    /// </summary>
    /// <returns>The built <see cref="DurableTaskFactory" />.</returns>
    internal DurableTaskFactory Build()
    {
        return new DurableTaskFactory(this.activitiesBuilder.ToImmutable(), this.orchestratorsBuilder.ToImmutable());
    }

    static TaskName GetTaskName(Type taskDeclarationType)
    {
        // IMPORTANT: This logic needs to be kept consistent with the source generator logic
        if (Attribute.GetCustomAttribute(taskDeclarationType, typeof(DurableTaskAttribute))
            is DurableTaskAttribute attribute)
        {
            return attribute.Name;
        }
        else
        {
            return taskDeclarationType.Name;
        }
    }
}
