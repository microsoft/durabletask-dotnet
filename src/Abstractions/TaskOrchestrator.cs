// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Dapr.DurableTask;

/// <summary>
/// Common interface for task orchestrator implementations.
/// </summary>
/// <remarks>
/// Users should not implement task orchestrators using this interface, directly.
/// Instead, <see cref="TaskOrchestrator{TInput, TOutput}"/> should be used to implement orchestrators.
/// </remarks>
public interface ITaskOrchestrator
{
    /// <summary>
    /// Gets the type of the input parameter that this orchestrator accepts.
    /// </summary>
    Type InputType { get; }

    /// <summary>
    /// Gets the type of the return value that this orchestrator produces.
    /// </summary>
    Type OutputType { get; }

    /// <summary>
    /// Invokes the task orchestrator with the specified context and input.
    /// </summary>
    /// <param name="context">The task orchestrator's context.</param>
    /// <param name="input">The task orchestrator's input.</param>
    /// <returns>Returns the orchestrator output as the result of a <see cref="Task"/>.</returns>
    Task<object?> RunAsync(TaskOrchestrationContext context, object? input);
}

/// <summary>
/// Base class for task orchestrator implementations.
/// </summary>
/// <remarks>
/// <para>
///  Task orchestrators describe how actions are executed and the order in which actions are executed. Orchestrators
///  don't call into external services or do complex computation directly. Rather, they delegate these tasks to
///  <em>activities</em>, which perform the actual work.
/// </para>
/// <para>
///   Orchestrators can be scheduled using an external client (see Dapr.DurableTask.Client). Orchestrators can
///   also invoke child orchestrators using the
///   <see cref="TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)"/> method.
///   Orchestrators that derive from <see cref="TaskOrchestrator{TInput, TOutput}"/> can also be invoked using
///   generated extension methods. To participate in code generation, an orchestrator class must be decorated with the
///   <see cref="DurableTaskAttribute"/> attribute. The source generator will then generate a
///   <c>ScheduleNewMyOrchestratorOrchestratorInstanceAsync()</c> extension method on <c>DurableTaskClient</c> for an
///   orchestrator named "MyOrchestrator". Similarly, a  <c>CallMyOrchestratorAsync()</c> extension method will be
///   generated on the <c>TaskOrchestrationContext</c> class for calling "MyOrchestrator" as a sub-orchestration. In
///   all cases, the generated input parameters and return values will be derived from <typeparamref name="TInput"/>
///   and <typeparamref name="TOutput"/> respectively.
/// </para>
/// <para>
///   Orchestrators may be replayed multiple times to rebuild their local state after being reloaded into memory.
///   Orchestrator code must therefore be <em>deterministic</em> to ensure no unexpected side effects from execution
///   replay. To account for this behavior, there are several coding constraints to be aware of:
///   <list type="bullet">
///     <item>
///       An orchestrator must not generate random numbers or random GUIDs, get the current date, read environment
///       variables, or do anything else that might result in a different value if the code is replayed in the future.
///       Activities and built-in properties and methods on the <see cref="TaskOrchestrationContext"/> parameter, like
///       <see cref="TaskOrchestrationContext.CurrentUtcDateTime"/> and <see cref="TaskOrchestrationContext.NewGuid"/>,
///       can be used to work around these restrictions.
///     </item>
///     <item>
///       Orchestrator logic must be executed on the orchestrator thread. Creating new threads, scheduling callbacks
///       on worker pool threads, or awaiting non-durable tasks is forbidden and may result in failures or other
///       unexpected behavior. Blocking the execution thread may also result in unexpected performance problems. The use
///       of <c>await</c> should be restricted to tasks from methods on the <see cref="TaskOrchestrationContext"/>
///       parameter object (i.e., "durable" tasks) or tasks that wrap these "durable" tasks, like
///       <see cref="Task.WhenAll(Task[])"/> and <see cref="Task.WhenAny(Task[])"/>.
///     </item>
///     <item>
///       Avoid infinite loops as they could cause the application to run out of memory. Instead, ensure that loops are
///       bounded or use <see cref="TaskOrchestrationContext.ContinueAsNew"/> to restart an orchestrator with a new
///       input.
///     </item>
///     <item>
///       When logging, ensure you use only loggers created from
///       <see cref="TaskOrchestrationContext.CreateReplaySafeLogger(string)"/> or other overloads, as this will ensure
///       logging is not duplicated on each replay.
///     </item>
///   </list>
/// </para>
/// <para>
///   Orchestrator code is tightly coupled with its execution history so special care must be taken when making changes
///   to orchestrator code. For example, adding or removing activity tasks to an orchestrator's code may cause a
///   mismatch between code and history for in-flight orchestrations. To avoid potential issues related to orchestrator
///   versioning, consider applying the following strategies:
///   <list type="bullet">
///     <item>
///       Deploy multiple versions of applications side-by-side allowing new code to run independently of old code.
///     </item>
///     <item>
///       Rather than changing existing orchestrators, create new orchestrators that implement the modified behavior.
///     </item>
///     <item>
///       Ensure all in-flight orchestrations are complete before applying code changes to existing orchestrator code.
///     </item>
///     <item>
///       If possible, only make changes to orchestrator code that won't impact its history or execution path. For
///       example, renaming variables or adding log statements have no impact on an orchestrator's execution path and
///       are safe to apply to existing orchestrations.
///     </item>
///   </list>
/// </para>
/// </remarks>
/// <typeparam name="TInput">The type of the input parameter that this orchestrator accepts.</typeparam>
/// <typeparam name="TOutput">The type of the return value that this orchestrator produces.</typeparam>
public abstract class TaskOrchestrator<TInput, TOutput> : ITaskOrchestrator
{
    /// <inheritdoc/>
    Type ITaskOrchestrator.InputType => typeof(TInput);

    /// <inheritdoc/>
    Type ITaskOrchestrator.OutputType => typeof(TOutput);

    /// <inheritdoc/>
    async Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
    {
        Check.NotNull(context, nameof(context));
        if (!IsValidInput(input, out TInput? typedInput))
        {
            throw new ArgumentException($"Input type '{input?.GetType()}' does not match expected type '{typeof(TInput)}'.");
        }

        return await this.RunAsync(context, typedInput);
    }

    /// <summary>
    /// Override to implement task orchestrator logic.
    /// </summary>
    /// <param name="context">The task orchestrator's context.</param>
    /// <param name="input">The deserialized orchestration input.</param>
    /// <returns>The output of the orchestration as a task.</returns>
    public abstract Task<TOutput> RunAsync(TaskOrchestrationContext context, TInput input);

    /// <summary>
    /// Due to nullable reference types being static analysis only, we need to do our best efforts for validating the
    /// input type, but also give control of nullability to the implementation. It is not ideal, but we do not want to
    /// force 'TInput?' on the RunAsync implementation.
    /// </summary>
    static bool IsValidInput(object? input, [NotNullWhen(true)] out TInput? typedInput)
    {
        if (input is TInput typed)
        {
            // Quick pattern check.
            typedInput = typed;
            return true;
        }
        else if (input is not null && typeof(TInput) != input.GetType())
        {
            typedInput = default;
            return false;
        }

        // Input is null and did not match a nullable value type. We do not have enough information to tell if it is
        // valid or not. We will have to defer this decision to the implementation. Additionally, we will coerce a null
        // input to a default value type here. This is to keep the two RunAsync(context, default) overloads to have
        // identical behavior.
        typedInput = default!;
        return true;
    }
}
