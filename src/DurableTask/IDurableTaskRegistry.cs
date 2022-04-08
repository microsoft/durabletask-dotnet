// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace Microsoft.DurableTask;

/// <summary>
/// Defines methods for registering orchestrators and activities with a host.
/// </summary>
public interface IDurableTaskRegistry
{
    /// <summary>
    /// Registers an orchestrator function that doesn't take any input nor returns any output.
    /// </summary>
    /// <inheritdoc cref="AddOrchestrator{TInput, TOutput}"/>
    public IDurableTaskRegistry AddOrchestrator(
        TaskName name,
        Func<TaskOrchestrationContext, Task> implementation);

    /// <summary>
    /// Registers an orchestrator function that returns an output but doesn't take any input.
    /// </summary>
    /// <inheritdoc cref="AddOrchestrator{TInput, TOutput}"/>
    public IDurableTaskRegistry AddOrchestrator<TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, Task<TOutput?>> implementation);

    /// <summary>
    /// Registers an orchestrator function that takes an input and returns an output.
    /// </summary>
    /// <typeparam name="TInput">The orchestrator function's input type.</typeparam>
    /// <typeparam name="TOutput">The orchestrator function's return type.</typeparam>
    /// <param name="name">The name of the orchestrator.</param>
    /// <param name="implementation">The orchestration implementation as a function.</param>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public IDurableTaskRegistry AddOrchestrator<TInput, TOutput>(
        TaskName name,
        Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation);

    /// <summary>
    /// Registers an orchestrator that implements the <see cref="ITaskOrchestrator"/> interface.
    /// </summary>
    /// <typeparam name="T">The concrete type of the orchestrator.</typeparam>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public IDurableTaskRegistry AddOrchestrator<T>() where T : ITaskOrchestrator;

    /// <summary>
    /// Registers an activity as a synchronous (blocking) lambda function that doesn't take any input nor returns any output.
    /// </summary>
    /// <param name="name">The name of the activity.</param>
    /// <param name="implementation">The lambda function to invoke when the activity is called.</param>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public IDurableTaskRegistry AddActivity(
        TaskName name,
        Action<TaskActivityContext> implementation);

    /// <summary>
    /// Registers an activity as an asynchronous (non-blocking) lambda function.
    /// </summary>
    /// <inheritdoc cref="AddActivity(TaskName, Action{TaskActivityContext})"/>
    public IDurableTaskRegistry AddActivity(
        TaskName name,
        Func<TaskActivityContext, Task> implementation);

    /// <summary>
    /// Registers an activity as an synchronous (blocking) lambda function with an input and an output.
    /// </summary>
    /// <typeparam name="TInput">The input type of the activity.</typeparam>
    /// <typeparam name="TOutput">The output type of the activity.</typeparam>
    /// <inheritdoc cref="AddActivity(TaskName, Action{TaskActivityContext})"/>
    public IDurableTaskRegistry AddActivity<TInput, TOutput>(
        TaskName name,
        Func<TaskActivityContext, TInput?, TOutput?> implementation);

    /// <summary>
    /// Registers an activity as an asynchronous (non-blocking) lambda function.
    /// </summary>
    /// <inheritdoc cref="AddActivity{TInput, TOutput}"/>
    public IDurableTaskRegistry AddActivity<TInput, TOutput>(
        TaskName name,
        Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation);

    /// <summary>
    /// Registers an activity as a type that implements the <see cref="ITaskActivity"/> interface.
    /// </summary>
    /// <typeparam name="T">The type that implements <see cref="ITaskActivity"/>.</typeparam>
    /// <returns>Returns this <see cref="IDurableTaskRegistry"/> instance.</returns>
    public IDurableTaskRegistry AddActivity<T>() where T : ITaskActivity;
}
