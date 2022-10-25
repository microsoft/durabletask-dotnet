// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Shims;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for <see cref="DurableTaskRegistry" />.
/// </summary>
public static partial class DurableTaskRegistryExtensions
{
    /// <summary>
    /// Registers an orchestrator factory.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="type">The orchestrator type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator(this DurableTaskRegistry registry, TaskName name, Type type)
    {
        // TODO: Compile a constructor expression for performance.
        Check.NotNull(registry);
        Check.ConcreteType<ITaskOrchestrator>(type);
        return registry.AddOrchestrator(name, () => (ITaskOrchestrator)Activator.CreateInstance(type));
    }

    /// <summary>
    /// Registers an orchestrator factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="type">The orchestrator type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator(this DurableTaskRegistry registry, Type type)
        => registry.AddOrchestrator(type.GetTaskName(), type);

    /// <summary>
    /// Registers an orchestrator factory.
    /// </summary>
    /// <typeparam name="TOrchestrator">The type of orchestrator to register.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TOrchestrator>(this DurableTaskRegistry registry, TaskName name)
        where TOrchestrator : class, ITaskOrchestrator
        => registry.AddOrchestrator(name, typeof(TOrchestrator));

    /// <summary>
    /// Registers an orchestrator factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <typeparam name="TOrchestrator">The type of orchestrator to register.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TOrchestrator>(this DurableTaskRegistry registry)
        where TOrchestrator : class, ITaskOrchestrator
        => registry.AddOrchestrator(typeof(TOrchestrator));

    /// <summary>
    /// Registers an orchestrator singleton.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator(
        this DurableTaskRegistry registry, TaskName name, ITaskOrchestrator orchestrator)
    {
        Check.NotNull(registry);
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator(name, () => orchestrator);
    }

    /// <summary>
    /// Registers an orchestrator singleton.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="orchestrator">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator(this DurableTaskRegistry registry, ITaskOrchestrator orchestrator)
    {
        Check.NotNull(registry);
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator(orchestrator.GetType().GetTaskName(), orchestrator);
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TInput, TOutput>(
        this DurableTaskRegistry registry,
        TaskName name,
        Func<TaskOrchestrationContext, TInput, Task<TOutput>> orchestrator)
    {
        Check.NotNull(registry);
        Check.NotNull(orchestrator);
        ITaskOrchestrator wrapper = FuncTaskOrchestrator.Create(orchestrator);
        return registry.AddOrchestrator(name, wrapper);
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TInput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskOrchestrationContext, TInput, Task> orchestrator)
    {
        Check.NotNull(registry);
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator<TInput, object?>(name, async (context, input) =>
        {
            await orchestrator(context, input);
            return null;
        });
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TInput, TOutput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskOrchestrationContext, TInput, TOutput> orchestrator)
    {
        Check.NotNull(registry);
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator<TInput, TOutput>(
            name, (context, input) => Task.FromResult(orchestrator.Invoke(context, input)));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TOutput>(
        this DurableTaskRegistry registry, TaskName name, Func<TaskOrchestrationContext, Task<TOutput>> orchestrator)
    {
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator<object?, TOutput>(name, (context, _) => orchestrator(context));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator<TInput>(
        this DurableTaskRegistry registry, TaskName name, Action<TaskOrchestrationContext, TInput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator<TInput, object?>(name, (context, input) =>
        {
            orchestrator(context, input);
            return CompletedNullTask;
        });
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <param name="registry">The registry to add to.</param>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public static DurableTaskRegistry AddOrchestrator(
        this DurableTaskRegistry registry, TaskName name, Action<TaskOrchestrationContext> orchestrator)
    {
        Check.NotNull(orchestrator);
        return registry.AddOrchestrator<object?, object?>(name, (context, input) =>
        {
            orchestrator(context);
            return CompletedNullTask;
        });
    }
}
