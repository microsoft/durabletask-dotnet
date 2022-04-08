// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

// TODO: Summary and detailed description in the remarks
public abstract class TaskOrchestrationContext
{
    /// <summary>
    /// Gets the name of the task orchestration.
    /// </summary>
    public abstract TaskName Name { get; }

    /// <summary>
    /// Gets the unique ID of the current orchestration instance.
    /// </summary>
    public abstract string InstanceId { get; }

    /// <summary>
    /// Gets the current orchestration time in UTC.
    /// </summary>
    /// <remarks>
    /// The current orchestration time is stored in the orchestration history and this API will
    /// return the same value each time it is called from a particular point in the orchestration's
    /// execution. It is a deterministic, replay-safe replacement for existing .NET APIs for getting
    /// the curren time, such as <see cref="DateTime.UtcNow"/> and <see cref="DateTimeOffset.UtcNow"/>.
    /// </remarks>
    public abstract DateTime CurrentDateTimeUtc { get; }

    /// <summary>
    /// Gets a value indicating whether the orchestrator is currently replaying a previous execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Orchestrator functions are "replayed" after being unloaded from memory to reconstruct local variable state.
    /// During a replay, previously executed tasks will be completed automatically with previously seen values
    /// that are stored in the orchestration history. One the orchestrator reaches the point in the orchestrator
    /// where it's no longer replaying existing history, the <see cref="IsReplaying"/> property will return <c>false</c>.
    /// </para><para>
    /// You can use this property if you have logic that needs to run only when *not* replaying. For example,
    /// certain types of application logging may become too noisy when duplicated as part of replay. The
    /// application code could check to see whether the function is being replayed and then issue the log statements
    /// when this value is <c>false</c>.
    /// </para>
    /// </remarks>
    /// <value>
    /// <c>true</c> if the orchestrator is currently replaying a previous execution; otherwise <c>false</c>.
    /// </value>
    public abstract bool IsReplaying { get; }

    // TODO: Summary and detailed remarks
    /// <param name="name">The name of the activity to call.</param>
    /// <param name="input">The serializable input to pass to the activity.</param>
    /// <param name="options">Additional options that control the execution and processing of the activity.</param>
    /// <returns>A task that completes when the activity completes or fails.</returns>
    /// <exception cref="ArgumentException">The specified orchestrator does not exist.</exception>
    public virtual Task CallActivityAsync(TaskName name, object? input = null, TaskOptions? options = null)
    {
        return this.CallActivityAsync<object>(name, input, options);
    }

    /// <returns>A task that completes when the activity completes or fails. The result of the task is the activity's return value.</returns>
    /// <inheritdoc cref="CallActivityAsync"/>
    public abstract Task<T> CallActivityAsync<T>(TaskName name, object? input = null, TaskOptions? options = null);

    // TODO: Summary
    /// <remarks>
    /// <para>
    /// Unlike named activities, anonymous activities are triggered in local memory and always run in the same process
    /// space as the calling orchestrators. If a machine failure occurs before the anonymous activity completes, then
    /// the previous orchestration execution will be re-run to re-schedule the anonymous activity.
    /// </para>
    /// </remarks>
    /// <inheritdoc cref="CallActivityAsync{T}"/>
    [Obsolete("This method is not yet fully implemented")]
    public abstract Task<T> CallActivityAsync<T>(Func<object?, T> activityLambda, object? input = null, TaskOptions? options = null);

    public virtual Task CreateTimer(TimeSpan delay, CancellationToken cancellationToken)
    {
        DateTime fireAt = this.CurrentDateTimeUtc.Add(delay);
        return this.CreateTimer(fireAt, cancellationToken);
    }

    public abstract Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for an event to be raised with name <paramref name="name"/> and returns the event data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// External clients can raise events to a waiting orchestration instance using
    /// the <see cref="DurableTaskClient.RaiseEventAsync"/> method.
    /// </para><para>
    /// If the current orchestrator instance is not yet waiting for an event named <paramref name="eventName"/>,
    /// then the event will be saved in the orchestration instance state and dispatched immediately when
    /// <see cref="WaitForExternalEvent{T}"/> is called. This event saving occurs even 
    /// if the current orchestrator cancels the wait operation before the event is received.
    /// </para><para>
    /// Orchestrators can wait for the same event name multiple times, so waiting for multiple events with the same name is
    /// allowed. Each external event received by an orchestrator will complete just one task returned by this method.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the event to wait for. Event names are case-insensitive. External event names can be reused any number of times; they are not required to be unique.</param>
    /// <param name="cancelToken">A <c>CancellationToken</c> to use to abort waiting for the event.</param>
    /// <typeparam name="T">Any serializable type that represents the event payload.</typeparam>
    /// <returns>A task that completes when the external event is received. The value of the task is the deserialized event payload.</returns>
    public abstract Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default);

    /// <param name="timeout">The amount of time to wait before cancelling the external event task.</param>
    /// <inheritdoc cref="WaitForExternalEvent(string, CancellationToken)"/>
    public async Task<T> WaitForExternalEvent<T>(string eventName, TimeSpan timeout)
    {
        // Timeouts are implemented using durable timers.
        using CancellationTokenSource timerCts = new();
        Task timeoutTask = this.CreateTimer(timeout, timerCts.Token);

        using CancellationTokenSource eventCts = new();
        Task<T> externalEventTask = this.WaitForExternalEvent<T>(eventName, eventCts.Token);

        // Wait for either task to complete and then cancel the one that didn't.
        Task winner = await Task.WhenAny(timeoutTask, externalEventTask);
        if (winner == externalEventTask)
        {
            timerCts.Cancel();
        }
        else
        {
            eventCts.Cancel();
        }

        // This will either return the received value or throw if the task was cancelled.
        return await externalEventTask;
    }

    /// <summary>
    /// Assigns a custom status value to the current orchestration.
    /// </summary>
    /// <remarks>
    /// The <paramref name="customStatus"/> value is serialized and stored in orchestration state and will
    /// be made available to the orchestration status query APIs, such as <see cref="DurableTaskClient.GetInstanceMetadata"/>.
    /// The serialized value must not exceed 16 KB of UTF-16 encoded text.
    /// </remarks>
    /// <param name="customStatus">
    /// A serializable value to assign as the custom status value or <c>null</c> to clear the custom status.
    /// </param>
    public abstract void SetCustomStatus(object? customStatus);

    /// <summary>
    /// Executes a named sub-orchestrator and returns the result.
    /// </summary>
    /// <typeparam name="TResult">
    /// The type into which to deserialize the sub-orchestrator's output.
    /// </typeparam>
    /// <inheritdoc cref="CallSubOrchestratorAsync(TaskName, string?, object?, TaskOptions?)"/>
    public abstract Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName,
        string? instanceId = null,
        object? input = null,
        TaskOptions? options = null);

    // TODO: Exception documentation for sub-orchestrator failures
    // TODO: Optional CancellationToken parameter
    /// <summary>
    /// Executes a named sub-orchestrator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In addition to activities, orchestrators can schedule other orchestrators, creating <i>sub-orchestrations</i>.
    /// A sub-orchestration has its own instance ID, history, and status that is independent of the parent orchestrator that started it.
    /// </para><para>
    /// Sub-orchestrations have many benefits:
    /// <list type="bullet">
    ///  <item>You can split large orchestrations into a series of smaller sub-orchestrations, making your code more maintainable.</item>
    ///  <item>You can distribute orchestration logic across multiple compute nodes concurrently, which is useful if your orchestration logic otherwise needs to coordinate a lot of tasks.</item>
    ///  <item>You can reduce memory usage and CPU overhead by keeping the history of parent orchestrations smaller.</item>
    /// </list>
    /// </para><para>
    /// The return value of a sub-orchestration is its output. If a sub-orchestration fails with an exception, then that exception will be surfaced to the parent orchestration, just like it is when an activity task fails with an exception. Sub-orchestrations also support automatic retry policies.
    /// </para><para>
    /// Because sub-orchestrations are independent of their parents, terminating a parent orchestration does not affect any sub-orchestrations.
    /// You must terminate each sub-orchestration independently using its instance ID, which is specified using the <paramref name="instanceId"/>
    /// parameter.
    /// </para>
    /// </remarks>
    /// <param name="orchestratorName">The name of the orchestrator to call.</param>
    /// <param name="instanceId">
    /// A unique ID to use for the sub-orchestration instance. If not specified, a random instance ID will be generated.
    /// </param>
    /// <param name="input">The serializable input to pass to the sub-orchestrator.</param>
    /// <param name="options">Additional options that control the execution and processing of the sub-orchestrator.</param>
    /// <returns>A task that completes when the sub-orchestrator completes or fails.</returns>
    /// <exception cref="ArgumentException">The specified orchestrator does not exist.</exception>
    public Task CallSubOrchestratorAsync(
        TaskName orchestratorName,
        string? instanceId = null,
        object? input = null,
        TaskOptions? options = null)
    {
        return this.CallSubOrchestratorAsync<object>(orchestratorName, instanceId, input, options);
    }

    /// <summary>
    /// Restarts the orchestration with a new input and clears its history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is primarily designed for eternal orchestrations, which are orchestrations that
    /// may not ever complete. It works by restarting the orchestration, providing it with a new input,
    /// and truncating the existing orchestration history. It allows an orchestration to continue
    /// running indefinitely without having its history grow unbounded. The benefits of periodically
    /// truncating history include decreased memory usage, decreased storage volumes, and shorter orchestrator
    /// replays when rebuilding state.
    /// </para><para>
    /// The results of any incomplete tasks will be discarded when an orchestrator calls
    /// <see cref="ContinueAsNew"/>. For example, if a timer is scheduled and then <see cref="ContinueAsNew"/>
    /// is called before the timer fires, the timer event will be discarded. The only exception to this
    /// is external events. By default, if an external event is received by an orchestration but not yet
    /// processed, the event is saved in the orchestration state unit it is received by a call to
    /// <see cref="WaitForExternalEvent{T}"/>. These events will continue to remain in memory even after
    /// an orchestrator restarts using <see cref="ContinueAsNew"/>. You can disable this behavior and
    /// remove any saved external events by specifying <c>false</c> for the <paramref name="preserveUnprocessedEvents"/>
    /// parameter value.
    /// </para><para>
    /// Orchestrator functions should return immediately after calling the <see cref="ContinueAsNew"/> method.
    /// </para>
    /// </remarks>
    /// <param name="newInput">The JSON-serializable input data to re-initialize the instance with.</param>
    /// <param name="preserveUnprocessedEvents">
    /// If set to <c>true</c>, re-adds any unprocessed external events into the new execution
    /// history when the orchestration instance restarts. If <c>false</c>, any unprocessed
    /// external events will be discarded when the orchestration instance restarts.
    /// </param>
    public abstract void ContinueAsNew(object newInput, bool preserveUnprocessedEvents = true);

    /// <summary>
    /// Returns an instance of <see cref="ILogger"/> that is replay-safe, meaning that the logger only
    /// writes logs when the orchestrator is not replaying previous history.
    /// </summary>
    /// <remarks>
    /// This method wraps the provider <paramref name="logger"/> instance with a new <see cref="ILogger"/>
    /// implementation that only writes log messages when <see cref="this.IsReplaying"/> is <c>false</c>.
    /// The resulting logger can be used normally in orchestrator code without needing to worry about duplicate
    /// log messages caused by orchestrator replays.
    /// </remarks>
    /// <param name="logger">The <see cref="ILogger"/> to be wrapped for use by the orchestration.</param>
    /// <returns>An instance of <see cref="ILogger"/> that wraps the specified <paramref name="logger"/>.</returns>
    public ILogger CreateReplaySafeLogger(ILogger logger)
    {
        return new ReplaySafeLogger(this, logger);
    }

    class ReplaySafeLogger : ILogger
    {
        readonly TaskOrchestrationContext context;
        readonly ILogger logger;

        internal ReplaySafeLogger(TaskOrchestrationContext context, ILogger logger)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable BeginScope<TState>(TState state) => this.logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => this.logger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!this.context.IsReplaying)
            {
                this.logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
