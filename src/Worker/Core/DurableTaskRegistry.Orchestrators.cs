// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Shims;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for the Durable Task worker.
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
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="type">The orchestrator type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(TaskName name, Type type)
    {
        // TODO: Compile a constructor expression for performance.
        Check.ConcreteType<ITaskOrchestrator>(type);
        return this.AddOrchestrator(name, () => (ITaskOrchestrator)Activator.CreateInstance(type));
    }

    /// <summary>
    /// Registers an orchestrator factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <param name="type">The orchestrator type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(Type type)
        => this.AddOrchestrator(type.GetTaskName(), type);

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
        return this.AddOrchestrator(name, () => orchestrator);
    }

    /// <summary>
    /// Registers an orchestrator singleton.
    /// </summary>
    /// <param name="orchestrator">The orchestration instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(ITaskOrchestrator orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator(orchestrator.GetType().GetTaskName(), orchestrator);
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator output type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator<TInput, TOutput>(
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
    public DurableTaskRegistry AddOrchestrator<TInput, TOutput>(
        TaskName name, Func<TaskOrchestrationContext, TInput, TOutput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<TInput, TOutput>(
            name, (context, input) => Task.FromResult(orchestrator.Invoke(context, input)));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator<TInput>(
        TaskName name, Func<TaskOrchestrationContext, TInput, Task> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<TInput, object?>(name, async (context, input) =>
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
    public DurableTaskRegistry AddOrchestrator<TOutput>(
        TaskName name, Func<TaskOrchestrationContext, Task<TOutput>> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<object?, TOutput>(name, (context, _) => orchestrator(context));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator(TaskName name, Func<TaskOrchestrationContext, Task> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<object?, object?>(name, async (context, _) =>
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
    public DurableTaskRegistry AddOrchestrator<TOutput>(
        TaskName name, Func<TaskOrchestrationContext, TOutput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<object?, TOutput>(name, (context, _) => orchestrator(context));
    }

    /// <summary>
    /// Registers an orchestrator factory, where the implementation is <paramref name="orchestrator" />.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator input type.</typeparam>
    /// <param name="name">The name of the orchestrator to register.</param>
    /// <param name="orchestrator">The orchestrator implementation.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddOrchestrator<TInput>(
        TaskName name, Action<TaskOrchestrationContext, TInput> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<TInput, object?>(name, (context, input) =>
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
    public DurableTaskRegistry AddOrchestrator(TaskName name, Action<TaskOrchestrationContext> orchestrator)
    {
        Check.NotNull(orchestrator);
        return this.AddOrchestrator<object?, object?>(name, (context, input) =>
        {
            orchestrator(context);
            return CompletedNullTask;
        });
    }
}
