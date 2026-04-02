// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Container for registered <see cref="ITaskOrchestrator" />, <see cref="ITaskActivity" />,
/// and <see cref="Microsoft.DurableTask.Entities.ITaskEntity"/> implementations.
/// </summary>
public partial class DurableTaskRegistry
{
    /*
      Covers the following ways to add orchestrators.
      by type:
        Type argument
        TaskName and Type argument
        TOrchestrator generic parameter
        TaskName and TOrchestrator generic parameter
        ITaskOrchestrator singleton
        TaskName and ITaskOrchestrator singleton

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
    /// Registers an orchestrator factory.
    /// </summary>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="version">The orchestrator version.</param>
    /// <param name="factory">The orchestrator factory.</param>
    /// <returns>This registry instance, for call chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>If <paramref name="name"/> is <c>default</c>.</item>
    ///   <item>If <paramref name="name" /> and <paramref name="version" /> are already registered.</item>
    ///   <item>If <paramref name="factory"/> is <c>null</c>.</item>
    /// </list>
    /// </exception>
    /// <remarks>
    /// Registration is version-aware in the registry. Version-based worker and factory resolution is introduced in
    /// later staged follow-up work.
    /// </remarks>
    public DurableTaskRegistry AddOrchestrator(TaskName name, TaskVersion version, Func<ITaskOrchestrator> factory)
    {
        Check.NotDefault(name);
        Check.NotNull(factory);

        OrchestratorVersionKey key = new(name, version);
        if (this.Orchestrators.ContainsKey(key))
        {
            throw new ArgumentException(
                $"An {nameof(ITaskOrchestrator)} named '{name}' with version '{version.Version ?? string.Empty}' is already added.",
                nameof(name));
        }

        this.Orchestrators.Add(key, _ => factory());
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
        => this.AddOrchestrator(name, default, factory);

    /// <summary>
    /// Registers an orchestrator factory.
    /// </summary>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="type">The orchestrator type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(TaskName name, Type type)
    {
        // TODO: Compile a constructor expression for performance.
        Check.ConcreteType<ITaskOrchestrator>(type);
        return this.AddOrchestrator(
            name,
            type.GetDurableTaskVersion(),
            () => (ITaskOrchestrator)Activator.CreateInstance(type));
    }

    /// <summary>
    /// Registers an orchestrator factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <param name="type">The orchestrator type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(Type type)
    {
        Check.ConcreteType<ITaskOrchestrator>(type);
        return this.AddOrchestrator(type.GetTaskName(), type.GetDurableTaskVersion(), () => (ITaskOrchestrator)Activator.CreateInstance(type));
    }

    /// <summary>
    /// Registers an orchestrator factory.
    /// </summary>
    /// <typeparam name="TOrchestrator">The type of orchestrator to register.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator<TOrchestrator>(TaskName name)
        where TOrchestrator : class, ITaskOrchestrator
        => this.AddOrchestrator(name, typeof(TOrchestrator));

    /// <summary>
    /// Registers an orchestrator factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <typeparam name="TOrchestrator">The type of orchestrator to register.</typeparam>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator<TOrchestrator>()
        where TOrchestrator : class, ITaskOrchestrator
        => this.AddOrchestrator(typeof(TOrchestrator));

    /// <summary>
    /// Registers an orchestrator singleton.
    /// </summary>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(TaskName name, ITaskOrchestrator orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator(name, orchestrator.GetType().GetDurableTaskVersion(), () => orchestrator);
    }

    /// <summary>
    /// Registers an orchestrator singleton.
    /// </summary>
    /// <param name="orchestrator">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(ITaskOrchestrator orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator(
            orchestrator.GetType().GetTaskName(),
            orchestrator.GetType().GetDurableTaskVersion(),
            () => orchestrator);
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc<TInput, TOutput>(
        TaskName name, Func<TaskOrchestrationContext, TInput, Task<TOutput>> orchestrator)
    {
        Check.NotNull(orchestrator);
        ITaskOrchestrator wrapper = FuncTaskOrchestrator.Create(orchestrator);
        return this.AddOrchestrator(name, wrapper);
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc<TInput, TOutput>(
        TaskName name, Func<TaskOrchestrationContext, TInput, TOutput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<TInput, TOutput>(
            name, (context, input) => Task.FromResult(orchestrator.Invoke(context, input)));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc<TInput>(
        TaskName name, Func<TaskOrchestrationContext, TInput, Task> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<TInput, object?>(name, async (context, input) =>
        {
            await orchestrator(context, input);
            return null;
        });
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc<TOutput>(
        TaskName name, Func<TaskOrchestrationContext, Task<TOutput>> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<object?, TOutput>(name, (context, _) => orchestrator(context));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc(TaskName name, Func<TaskOrchestrationContext, Task> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<object?, object?>(name, async (context, _) =>
        {
            await orchestrator(context);
            return null;
        });
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc<TOutput>(
        TaskName name, Func<TaskOrchestrationContext, TOutput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<object?, TOutput>(name, (context, _) => orchestrator(context));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc<TInput>(
        TaskName name, Action<TaskOrchestrationContext, TInput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<TInput, object?>(name, (context, input) =>
        {
            orchestrator(context, input);
            return CompletedNullTask;
        });
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestratorFunc(TaskName name, Action<TaskOrchestrationContext> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestratorFunc<object?, object?>(name, (context, input) =>
        {
            orchestrator(context);
            return CompletedNullTask;
        });
    }
}
