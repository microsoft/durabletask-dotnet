// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Context object used by orchestrators to perform actions such as scheduling tasks, durable timers, waiting for
/// external events, and for getting basic information about the current orchestration.
/// </summary>
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
    /// Gets the version of the current orchestration instance.
    /// </summary>
    public abstract string InstanceVersion { get; }

    /// <summary>
    /// Gets the parent instance or <c>null</c> if there is no parent orchestration.
    /// </summary>
    public abstract ParentOrchestrationInstance? Parent { get; }

    /// <summary>
    /// Gets the current orchestration time in UTC.
    /// </summary>
    /// <remarks>
    /// The current orchestration time is stored in the orchestration history and this API will
    /// return the same value each time it is called from a particular point in the orchestration's
    /// execution. It is a deterministic, replay-safe replacement for existing .NET APIs for getting
    /// the current time, such as <see cref="DateTime.UtcNow"/> and <see cref="DateTimeOffset.UtcNow"/>.
    /// </remarks>
    public abstract DateTime CurrentUtcDateTime { get; }

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
    /// You can use this property if you have logic that needs to run only when <em>not</em> replaying. For example,
    /// certain types of application logging may become too noisy when duplicated as part of replay. The
    /// application code could check to see whether the function is being replayed and then issue the log statements
    /// when this value is <c>false</c>.
    /// </para>
    /// </remarks>
    /// <value>
    /// <c>true</c> if the orchestrator is currently replaying a previous execution; otherwise <c>false</c>.
    /// </value>
    public abstract bool IsReplaying { get; }

    /// <summary>
    /// Gets the entity feature, for interacting with entities.
    /// </summary>
    public virtual TaskOrchestrationEntityFeature Entities =>
        throw new NotSupportedException($"Durable entities are not supported by {this.GetType()}.");

    /// <summary>
    /// Gets the logger factory for this context.
    /// </summary>
    protected abstract ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the deserialized input of the orchestrator.
    /// </summary>
    /// <typeparam name="T">The expected type of the orchestrator input.</typeparam>
    /// <returns>
    /// Returns the deserialized input as an object of type <typeparamref name="T"/> or <c>null</c> if no input was
    /// provided.
    /// </returns>
    public abstract T? GetInput<T>(); // NOTE: This API is redundant and may be removed in a future version.

    /// <summary>
    /// Asynchronously invokes an activity by name and with the specified input value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Activities are the basic unit of work in a durable task orchestration. Unlike orchestrators, which are not
    /// allowed to do any I/O or call non-deterministic APIs, activities have no implementation restrictions.
    /// </para><para>
    /// An activity may execute in the local machine or a remote machine. The exact behavior depends on the underlying
    /// storage provider, which is responsible for distributing tasks across machines. In general, you should never make
    /// any assumptions about where an activity will run. You should also assume at-least-once execution guarantees for
    /// activities, meaning that an activity may be executed twice if, for example, there is a process failure before
    /// the activities result is saved into storage.
    /// </para><para>
    /// Both the inputs and outputs of activities are serialized and stored in durable storage. It's highly recommended
    /// to not include any sensitive data in activity inputs or outputs. It's also recommended to not use large payloads
    /// for activity inputs and outputs, which can result in expensive serialization and network utilization. For data
    /// that cannot be cheaply or safely persisted to storage, it's recommended to instead pass <em>references</em>
    /// (for example, a URL to a storage blob) to the data and have activities fetch the data directly as part of their
    /// implementation.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the activity to call.</param>
    /// <param name="input">The serializable input to pass to the activity.</param>
    /// <param name="options">Additional options that control the execution and processing of the activity.</param>
    /// <returns>A task that completes when the activity completes or fails.</returns>
    /// <exception cref="ArgumentException">The specified activity does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the calling thread is anything other than the main orchestrator thread.
    /// </exception>
    /// <exception cref="TaskFailedException">
    /// The activity failed with an unhandled exception. The details of the failure can be found in the
    /// <see cref="TaskFailedException.FailureDetails"/> property.
    /// </exception>
    public virtual Task CallActivityAsync(TaskName name, object? input = null, TaskOptions? options = null)
        => this.CallActivityAsync<object>(name, input, options);

    /// <returns>
    /// A task that completes when the activity completes or fails. The result of the task is the activity's return value.
    /// </returns>
    /// <inheritdoc cref="CallActivityAsync(TaskName, object?, TaskOptions?)"/>
    public virtual Task CallActivityAsync(TaskName name, TaskOptions options)
        => this.CallActivityAsync(name, null, options);

    /// <returns>
    /// A task that completes when the activity completes or fails. The result of the task is the activity's return value.
    /// </returns>
    /// <typeparam name="TResult">The type into which to deserialize the activity's output.</typeparam>
    /// <inheritdoc cref="CallActivityAsync(TaskName, object?, TaskOptions?)"/>
    public virtual Task<TResult> CallActivityAsync<TResult>(TaskName name, TaskOptions options)
        => this.CallActivityAsync<TResult>(name, null, options);

    /// <returns>
    /// A task that completes when the activity completes or fails. The result of the task is the activity's return value.
    /// </returns>
    /// <typeparam name="TResult">The type into which to deserialize the activity's output.</typeparam>
    /// <inheritdoc cref="CallActivityAsync(TaskName, object?, TaskOptions?)"/>
    public abstract Task<TResult> CallActivityAsync<TResult>(
        TaskName name, object? input = null, TaskOptions? options = null);

    /// <summary>
    /// Creates a durable timer that expires after the specified delay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All durable timers created using this method must either expire or be cancelled using the
    /// <paramref name="cancellationToken"/> before the orchestrator can complete. If unexpired timers exist when the
    /// orchestration completes, it will remain in the "Running" state until the scheduled expiration time.
    /// </para><para>
    /// Specifying a long delay (for example, a delay of a few days or more) may result in the creation of multiple,
    /// internally-managed durable timers. The orchestration code doesn't need to be aware of this behavior. However,
    /// it may be visible in framework logs and the stored history state.
    /// </para>
    /// </remarks>
    /// <param name="delay">The amount of time before the timer should expire.</param>
    /// <param name="cancellationToken">Used to cancel the durable timer.</param>
    /// <returns>A task that completes when the durable timer expires.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the calling thread is anything other than the main orchestrator thread.
    /// </exception>
    public virtual Task CreateTimer(TimeSpan delay, CancellationToken cancellationToken)
    {
        DateTime fireAt = this.CurrentUtcDateTime.Add(delay);
        return this.CreateTimer(fireAt, cancellationToken);
    }

    /// <summary>
    /// Creates a durable timer that expires at a set date and time.
    /// </summary>
    /// <param name="fireAt">The time at which the timer should expire.</param>
    /// <param name="cancellationToken">Used to cancel the durable timer.</param>
    /// <inheritdoc cref="CreateTimer(TimeSpan, CancellationToken)"/>
    public abstract Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken);

    /// <summary>
    /// Waits for an event to be raised with name <paramref name="eventName"/> and returns the event data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// External clients can raise events to a waiting orchestration instance. Similarly, orchestrations can raise
    /// events to other orchestrations using the <see cref="SendEvent"/> method.
    /// </para><para>
    /// If the current orchestrator instance is not yet waiting for an event named <paramref name="eventName"/>,
    /// then the event will be saved in the orchestration instance state and dispatched immediately when this method is
    /// called. This event saving occurs even if the current orchestrator cancels the wait operation before the event is
    /// received.
    /// </para><para>
    /// Orchestrators can wait for the same event name multiple times, so waiting for multiple events with the same name
    /// is allowed. Each external event received by an orchestrator will complete just one task returned by this method.
    /// </para>
    /// </remarks>
    /// <param name="eventName">
    /// The name of the event to wait for. Event names are case-insensitive. External event names can be reused any
    /// number of times; they are not required to be unique.
    /// </param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to use to abort waiting for the event.</param>
    /// <typeparam name="T">Any serializable type that represents the event payload.</typeparam>
    /// <returns>
    /// A task that completes when the external event is received. The value of the task is the deserialized event payload.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the calling thread is anything other than the main orchestrator thread.
    /// </exception>
    public abstract Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default);

    /// <param name="eventName">
    /// The name of the event to wait for. Event names are case-insensitive. External event names can be reused any
    /// number of times; they are not required to be unique.
    /// </param>
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
    /// Raises an external event for the specified orchestration instance.
    /// </summary>
    /// <remarks>
    /// <para>The target orchestration can handle the sent event using the
    /// <see cref="WaitForExternalEvent{T}(string, CancellationToken)"/> method.
    /// </para><para>
    /// If the target orchestration doesn't exist, the event will be silently dropped.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The ID of the orchestration instance to send the event to.</param>
    /// <param name="eventName">The name of the event to wait for. Event names are case-insensitive.</param>
    /// <param name="payload">The serializable payload of the external event.</param>
    public abstract void SendEvent(string instanceId, string eventName, object payload);

    /// <summary>
    /// Assigns a custom status value to the current orchestration.
    /// </summary>
    /// <remarks>
    /// The <paramref name="customStatus"/> value is serialized and stored in orchestration state and will
    /// be made available to the orchestration status query APIs. The serialized value must not exceed
    /// 16 KB of UTF-16 encoded text.
    /// </remarks>
    /// <param name="customStatus">
    /// A serializable value to assign as the custom status value or <c>null</c> to clear the custom status.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the calling thread is anything other than the main orchestrator thread.
    /// </exception>
    public abstract void SetCustomStatus(object? customStatus);

    /// <summary>
    /// Executes a named sub-orchestrator and returns the result.
    /// </summary>
    /// <typeparam name="TResult">The type into which to deserialize the sub-orchestrator's output.</typeparam>
    /// <inheritdoc cref="CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)"/>
    public abstract Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName, object? input = null, TaskOptions? options = null);

    /// <summary>
    /// Executes a named sub-orchestrator and returns the result.
    /// </summary>
    /// <typeparam name="TResult">The type into which to deserialize the sub-orchestrator's output.</typeparam>
    /// <inheritdoc cref="CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)"/>
    public virtual Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName orchestratorName, TaskOptions options)
        => this.CallSubOrchestratorAsync<TResult>(orchestratorName, null, options);

    /// <summary>
    /// Executes a named sub-orchestrator and returns the result.
    /// </summary>
    /// <inheritdoc cref="CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)"/>
    public virtual Task CallSubOrchestratorAsync(TaskName orchestratorName, TaskOptions options)
        => this.CallSubOrchestratorAsync(orchestratorName, null, options);

    /// <summary>
    /// Executes a named sub-orchestrator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In addition to activities, orchestrators can schedule other orchestrators, creating <i>sub-orchestrations</i>.
    /// A sub-orchestration has its own instance ID, history, and status that is independent of the parent orchestrator
    /// that started it.
    /// </para><para>
    /// Sub-orchestrations have many benefits:
    /// <list type="bullet">
    ///  <item>You can split large orchestrations into a series of smaller sub-orchestrations, making your code more
    ///  maintainable.</item>
    ///  <item>You can distribute orchestration logic across multiple compute nodes concurrently, which is useful if
    ///  your orchestration logic otherwise needs to coordinate a lot of tasks.</item>
    ///  <item>You can reduce memory usage and CPU overhead by keeping the history of parent orchestrations smaller.</item>
    /// </list>
    /// </para><para>
    /// The return value of a sub-orchestration is its output. If a sub-orchestration fails with an exception, then that
    /// exception will be surfaced to the parent orchestration, just like it is when an activity task fails with an
    /// exception. Sub-orchestrations also support automatic retry policies.
    /// </para><para>
    /// Because sub-orchestrations are independent of their parents, terminating a parent orchestration does not affect
    /// any sub-orchestrations. You must terminate each sub-orchestration independently using its instance ID, which is
    /// specified by supplying <see cref="SubOrchestrationOptions" /> in place of <see cref="TaskOptions" />.
    /// </para>
    /// </remarks>
    /// <param name="orchestratorName">The name of the orchestrator to call.</param>
    /// <param name="input">The serializable input to pass to the sub-orchestrator.</param>
    /// <param name="options">
    /// Additional options that control the execution and processing of the sub-orchestrator. Callers can choose to
    /// supply the derived type <see cref="SubOrchestrationOptions" />.
    /// </param>
    /// <returns>A task that completes when the sub-orchestrator completes or fails.</returns>
    /// <exception cref="ArgumentException">The specified orchestrator does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the calling thread is anything other than the main orchestrator thread.
    /// </exception>
    /// <exception cref="TaskFailedException">
    /// The sub-orchestration failed with an unhandled exception. The details of the failure can be found in the
    /// <see cref="TaskFailedException.FailureDetails"/> property.
    /// </exception>
    public virtual Task CallSubOrchestratorAsync(
        TaskName orchestratorName, object? input = null, TaskOptions? options = null)
        => this.CallSubOrchestratorAsync<object>(orchestratorName, input, options);

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
    /// <see cref="WaitForExternalEvent{T}(string, CancellationToken)"/>. These events will continue to remain in memory
    /// even after an orchestrator restarts using <see cref="ContinueAsNew"/>. You can disable this behavior and
    /// remove any saved external events by specifying <c>false</c> for the <paramref name="preserveUnprocessedEvents"/>
    /// parameter value.
    /// </para><para>
    /// Orchestrator implementations should complete immediately after calling the <see cref="ContinueAsNew"/> method.
    /// </para>
    /// </remarks>
    /// <param name="newInput">The JSON-serializable input data to re-initialize the instance with.</param>
    /// <param name="preserveUnprocessedEvents">
    /// If set to <c>true</c>, re-adds any unprocessed external events into the new execution
    /// history when the orchestration instance restarts. If <c>false</c>, any unprocessed
    /// external events will be discarded when the orchestration instance restarts.
    /// </param>
    public abstract void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true);

    /// <summary>
    /// Creates a new GUID that is safe for replay within an orchestration or operation.
    /// </summary>
    /// <remarks>
    /// The default implementation of this method creates a name-based UUID V5 using the algorithm from RFC 4122 §4.3.
    /// The name input used to generate this value is a combination of the orchestration instance ID, the current time,
    /// and an internally managed sequence number.
    /// </remarks>
    /// <returns>The new <see cref="Guid"/> value.</returns>
    public abstract Guid NewGuid();

    /// <summary>
    /// Returns an instance of <see cref="ILogger"/> that is replay-safe, meaning that the logger only
    /// writes logs when the orchestrator is not replaying previous history.
    /// </summary>
    /// <param name="categoryName">The logger's category name.</param>
    /// <returns>An instance of <see cref="ILogger"/> that is replay-safe.</returns>
    public ILogger CreateReplaySafeLogger(string categoryName)
        => new ReplaySafeLogger(this, this.LoggerFactory.CreateLogger(categoryName));

    /// <inheritdoc cref="CreateReplaySafeLogger(string)" />
    /// <param name="type">The type to derive the category name from.</param>
    public virtual ILogger CreateReplaySafeLogger(Type type)
        => new ReplaySafeLogger(this, this.LoggerFactory.CreateLogger(type));

    /// <inheritdoc cref="CreateReplaySafeLogger(string)" />
    /// <typeparam name="T">The type to derive category name from.</typeparam>
    public virtual ILogger CreateReplaySafeLogger<T>()
        => new ReplaySafeLogger(this, this.LoggerFactory.CreateLogger<T>());

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

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!this.context.IsReplaying)
            {
                this.logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
