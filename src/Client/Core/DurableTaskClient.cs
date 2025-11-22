// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Internal;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Base class that defines client operations for managing durable task instances.
/// </summary>
/// <remarks>
/// <para>
/// Instances of <see cref="DurableTaskClient"/> can be used to start, query, raise events to, and terminate
/// orchestration instances. In most cases, methods on this class accept an instance ID as a parameter, which identifies
/// the orchestration instance.
/// </para><para>
/// At the time of writing, the most common implementation of this class is the gRPC client, which works by making gRPC
/// calls to a remote service (e.g. a sidecar) that implements the operation behavior. To ensure any owned network
/// resources are properly released, instances of <see cref="DurableTaskClient"/> should be disposed when they are no
/// longer needed.
/// </para><para>
/// Instances of this class are expected to be safe for multithreaded apps. You can therefore safely cache instances
/// of this class and reuse them across multiple contexts. Caching these objects is useful to improve overall
/// performance.
/// </para>
/// </remarks>
public abstract class DurableTaskClient : IOrchestrationSubmitter, IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    protected DurableTaskClient(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// Gets the name of the client.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the <see cref="DurableEntityClient"/> for interacting with durable entities.
    /// </summary>
    /// <remarks>
    /// Not all clients support durable entities. Refer to a specific client implementation for verifying support.
    /// </remarks>
    public virtual DurableEntityClient Entities =>
        throw new NotSupportedException($"{this.GetType()} does not support durable entities.");

    /// <inheritdoc cref="ScheduleNewOrchestrationInstanceAsync(TaskName, object, StartOrchestrationOptions, CancellationToken)"/>
    public virtual Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName, CancellationToken cancellation)
        => this.ScheduleNewOrchestrationInstanceAsync(orchestratorName, null, null, cancellation);

    /// <inheritdoc cref="ScheduleNewOrchestrationInstanceAsync(TaskName, object, StartOrchestrationOptions, CancellationToken)"/>
    public virtual Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName, object? input, CancellationToken cancellation)
        => this.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input, null, cancellation);

    /// <inheritdoc cref="ScheduleNewOrchestrationInstanceAsync(TaskName, object, StartOrchestrationOptions, CancellationToken)"/>
    public virtual Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName, StartOrchestrationOptions options, CancellationToken cancellation = default)
        => this.ScheduleNewOrchestrationInstanceAsync(orchestratorName, null, options, cancellation);

    /// <summary>
    /// Schedules a new orchestration instance for execution.
    /// </summary>
    /// <remarks>
    /// <para>All orchestrations must have a unique instance ID. You can provide an instance ID using the
    /// <paramref name="options"/> parameter or you can omit this and a random instance ID will be
    /// generated for you automatically. If an orchestration with the specified instance ID already exists and is in a
    /// non-terminal state (Pending, Running, etc.), then this operation may fail silently. However, if an orchestration
    /// instance with this ID already exists in a terminal state (Completed, Terminated, Failed, etc.) then the instance
    /// may be recreated automatically, depending on the configuration of the backend instance store.
    /// </para><para>
    /// Orchestration instances started with this method will be created in the
    /// <see cref="OrchestrationRuntimeStatus.Pending"/> state and will transition to the
    /// <see cref="OrchestrationRuntimeStatus.Running"/> after successfully awaiting its first task. The exact time it
    /// takes before a scheduled orchestration starts running depends on several factors, including the configuration
    /// and health of the backend task hub, and whether a start time was provided via <paramref name="options" />.
    /// </para><para>
    /// The task associated with this method completes after the orchestration instance was successfully scheduled. You
    /// can use the <see cref="GetInstanceAsync(string, bool, CancellationToken)"/> to query the status of the
    /// scheduled instance, the <see cref="WaitForInstanceStartAsync(string, bool, CancellationToken)"/> method to wait
    /// for the instance to transition out of the <see cref="OrchestrationRuntimeStatus.Pending"/> status, or the
    /// <see cref="WaitForInstanceCompletionAsync(string, bool, CancellationToken)"/> method to wait for the instance to
    /// reach a terminal state (Completed, Terminated, Failed, etc.).
    /// </para>
    /// </remarks>
    /// <param name="orchestratorName">The name of the orchestrator to schedule.</param>
    /// <param name="input">
    /// The optional input to pass to the scheduled orchestration instance. This must be a serializable value.
    /// </param>
    /// <param name="options">The options to start the new orchestration with.</param>
    /// <param name="cancellation">
    /// The cancellation token. This only cancels enqueueing the new orchestration to the backend. Does not cancel the
    /// orchestration once enqueued.
    /// </param>
    /// <returns>
    /// A task that completes when the orchestration instance is successfully scheduled. The value of this task is
    /// the instance ID of the scheduled orchestration instance. If a non-null instance ID was provided via
    /// <paramref name="options" />, the same value will be returned by the completed task.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="orchestratorName"/> is empty.</exception>
    public abstract Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default);

    /// <inheritdoc cref="RaiseEventAsync(string, string, object, CancellationToken)"/>
    public virtual Task RaiseEventAsync(
        string instanceId, string eventName, CancellationToken cancellation)
        => this.RaiseEventAsync(instanceId, eventName, null, cancellation);

    /// <summary>
    /// Sends an event notification message to a waiting orchestration instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In order to handle the event, the target orchestration instance must be waiting for an
    /// event named <paramref name="eventName"/> using the
    /// <see cref="TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)"/> API.
    /// If the target orchestration instance is not yet waiting for an event named <paramref name="eventName"/>,
    /// then the event will be saved in the orchestration instance state and dispatched immediately when the
    /// orchestrator calls <see cref="TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)"/>.
    /// This event saving occurs even if the orchestrator has canceled its wait operation before the event was received.
    /// </para><para>
    /// Orchestrators can wait for the same event name multiple times, so sending multiple events with the same name is
    /// allowed. Each external event received by an orchestrator will complete just one task returned by the
    /// <see cref="TaskOrchestrationContext.WaitForExternalEvent{T}(string, CancellationToken)"/> method.
    /// </para><para>
    /// Raised events for a completed or non-existent orchestration instance will be silently discarded.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The ID of the orchestration instance that will handle the event.</param>
    /// <param name="eventName">The name of the event. Event names are case-insensitive.</param>
    /// <param name="eventPayload">The serializable data payload to include with the event.</param>
    /// <param name="cancellation">
    /// The cancellation token. This only cancels enqueueing the event to the backend. Does not abort sending the event
    /// once enqueued.
    /// </param>
    /// <returns>A task that completes when the event notification message has been enqueued.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="instanceId"/> or <paramref name="eventName"/> is null or empty.
    /// </exception>
    public abstract Task RaiseEventAsync(
        string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default);

    /// <inheritdoc cref="WaitForInstanceStartAsync(string, bool, CancellationToken)"/>
    public virtual Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, CancellationToken cancellation)
        => this.WaitForInstanceStartAsync(instanceId, false, cancellation);

    /// <summary>
    /// Waits for an orchestration to start running and returns a <see cref="OrchestrationMetadata"/>
    /// object that contains metadata about the started instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A "started" orchestration instance is any instance not in the <see cref="OrchestrationRuntimeStatus.Pending"/>
    /// state.
    /// </para><para>
    /// If an orchestration instance is already running when this method is called, the method will return immediately.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The unique ID of the orchestration instance to wait for.</param>
    /// <param name="getInputsAndOutputs">
    /// Specify <c>true</c> to fetch the orchestration instance's inputs, outputs, and custom status, or <c>false</c> to
    /// omit them. The default value is <c>false</c> to minimize the network bandwidth, serialization, and memory costs
    /// associated with fetching the instance metadata.
    /// </param>
    /// <param name="cancellation">A <see cref="CancellationToken"/> that can be used to cancel the wait operation.</param>
    /// <returns>
    /// Returns a <see cref="OrchestrationMetadata"/> record that describes the orchestration instance and its execution
    /// status or <c>null</c> if no instance with ID <paramref name="instanceId"/> is found.
    /// </returns>
    public abstract Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default);

    /// <inheritdoc cref="WaitForInstanceCompletionAsync(string, bool, CancellationToken)"/>
    public virtual Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, CancellationToken cancellation)
        => this.WaitForInstanceCompletionAsync(instanceId, false, cancellation);

    /// <summary>
    /// Waits for an orchestration to complete and returns a <see cref="OrchestrationMetadata"/>
    /// object that contains metadata about the started instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A "completed" orchestration instance is any instance in one of the terminal states. For example, the
    /// <see cref="OrchestrationRuntimeStatus.Completed"/>, <see cref="OrchestrationRuntimeStatus.Failed"/>, or
    /// <see cref="OrchestrationRuntimeStatus.Terminated"/> states.
    /// </para><para>
    /// Orchestrations are long-running and could take hours, days, or months before completing.
    /// Orchestrations can also be eternal, in which case they'll never complete unless terminated.
    /// In such cases, this call may block indefinitely, so care must be taken to ensure appropriate timeouts are
    /// enforced using the <paramref name="cancellation"/> parameter.
    /// </para><para>
    /// If an orchestration instance is already complete when this method is called, the method will return immediately.
    /// </para>
    /// </remarks>
    /// <inheritdoc cref="WaitForInstanceStartAsync(string, bool, CancellationToken)"/>
    public abstract Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default);

    /// <inheritdoc cref="TerminateInstanceAsync(string, TerminateInstanceOptions, CancellationToken)"/>
    public virtual Task TerminateInstanceAsync(string instanceId, CancellationToken cancellation)
        => this.TerminateInstanceAsync(instanceId, null, cancellation);

    /// <inheritdoc cref="TerminateInstanceAsync(string, TerminateInstanceOptions, CancellationToken)"/>
    public virtual Task TerminateInstanceAsync(string instanceId, object? output, CancellationToken cancellation = default)
    {
        TerminateInstanceOptions? options = output is null ? null : new() { Output = output };
        return this.TerminateInstanceAsync(instanceId, options, cancellation);
    }

    /// <summary>
    /// Terminates an orchestration instance and updates its runtime status to
    /// <see cref="OrchestrationRuntimeStatus.Terminated"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method internally enqueues a "terminate" message in the task hub. When the task hub worker processes
    /// this message, it will update the runtime status of the target instance to
    /// <see cref="OrchestrationRuntimeStatus.Terminated"/>. You can use the
    /// <see cref="WaitForInstanceCompletionAsync(string, bool, CancellationToken)"/> to wait for the instance to reach
    /// the terminated state.
    /// </para>
    /// <para>
    /// Terminating an orchestration by default will not terminate any of the child sub-orchestrations that were started by
    /// the orchetration instance. If you want to terminate sub-orchestration instances as well, you can set <see cref="TerminateInstanceOptions.Recursive"/>
    /// flag to true which will enable termination of child sub-orchestration instances. It is set to false by default.
    /// Terminating an orchestration instance has no effect on any in-flight activity function executions
    /// that were started by the terminated instance. Those actions will continue to run
    /// without interruption. However, their results will be discarded.
    /// </para><para>
    /// At the time of writing, there is no way to terminate an in-flight activity execution.
    /// </para><para>
    /// Attempting to terminate a completed or non-existent orchestration instance will fail silently.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The ID of the orchestration instance to terminate.</param>
    /// <param name="options">The optional options for terminating the orchestration.</param>
    /// <param name="cancellation">
    /// The cancellation token. This only cancels enqueueing the termination request to the backend. Does not abort
    /// termination of the orchestration once enqueued.
    /// </param>
    /// <returns>A task that completes when the terminate message is enqueued.</returns>
    public virtual Task TerminateInstanceAsync(string instanceId, TerminateInstanceOptions? options = null, CancellationToken cancellation = default)
        => throw new NotSupportedException($"{this.GetType()} does not support orchestration termination.");

    /// <inheritdoc cref="SuspendInstanceAsync(string, string, CancellationToken)"/>
    public virtual Task SuspendInstanceAsync(string instanceId, CancellationToken cancellation)
        => this.SuspendInstanceAsync(instanceId, null, cancellation);

    /// <summary>
    /// Suspends an orchestration instance, halting processing of it until
    /// <see cref="ResumeInstanceAsync(string, string, CancellationToken)" /> is used to resume the orchestration.
    /// </summary>
    /// <param name="instanceId">The instance ID of the orchestration to suspend.</param>
    /// <param name="reason">The optional suspension reason.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the suspend operation. Note, cancelling this token
    /// does <b>not</b> resume the orchestration if suspend was successful.
    /// </param>
    /// <returns>A task that completes when the suspend has been committed to the backend.</returns>
    public abstract Task SuspendInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default);

    /// <inheritdoc cref="ResumeInstanceAsync(string, string, CancellationToken)"/>
    public virtual Task ResumeInstanceAsync(string instanceId, CancellationToken cancellation)
        => this.ResumeInstanceAsync(instanceId, null, cancellation);

    /// <summary>
    /// Resumes an orchestration instance that was suspended via <see cref="SuspendInstanceAsync(string, string, CancellationToken)" />.
    /// </summary>
    /// <param name="instanceId">The instance ID of the orchestration to resume.</param>
    /// <param name="reason">The optional resume reason.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the resume operation. Note, cancelling this token
    /// does <b>not</b> re-suspend the orchestration if resume was successful.
    /// </param>
    /// <returns>A task that completes when the resume has been committed to the backend.</returns>
    public abstract Task ResumeInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default);

    /// <inheritdoc cref="GetInstanceAsync(string, bool, CancellationToken)"/>
    public virtual Task<OrchestrationMetadata?> GetInstanceAsync(
        string instanceId, CancellationToken cancellation)
        => this.GetInstanceAsync(instanceId, false, cancellation);

    /// <summary>
    /// Fetches orchestration instance metadata from the configured durable store.
    /// </summary>
    /// <remarks>
    /// You can use the <paramref name="getInputsAndOutputs"/> parameter to determine whether to fetch input and
    /// output data for the target orchestration instance. If your code doesn't require access to this data, it's
    /// recommended that you set this parameter to <c>false</c> to minimize the network bandwidth, serialization, and
    /// memory costs associated with fetching the instance metadata.
    /// </remarks>
    /// <inheritdoc cref="WaitForInstanceStartAsync(string, bool, CancellationToken)"/>
    public virtual Task<OrchestrationMetadata?> GetInstanceAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        => this.GetInstancesAsync(instanceId, getInputsAndOutputs, cancellation);

    /// <inheritdoc cref="GetInstancesAsync(string, bool, CancellationToken)"/>
    [EditorBrowsable(EditorBrowsableState.Never)] // use GetInstanceAsync
    public virtual Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId, CancellationToken cancellation)
        => this.GetInstancesAsync(instanceId, false, cancellation);

    /// <summary>
    /// Fetches orchestration instance metadata from the configured durable store.
    /// </summary>
    /// <remarks>
    /// You can use the <paramref name="getInputsAndOutputs"/> parameter to determine whether to fetch input and
    /// output data for the target orchestration instance. If your code doesn't require access to this data, it's
    /// recommended that you set this parameter to <c>false</c> to minimize the network bandwidth, serialization, and
    /// memory costs associated with fetching the instance metadata.
    /// </remarks>
    /// <inheritdoc cref="WaitForInstanceStartAsync(string, bool, CancellationToken)"/>
    [EditorBrowsable(EditorBrowsableState.Never)] // use GetInstanceAsync
    public abstract Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default);

    /// <summary>
    /// Queries orchestration instances.
    /// </summary>
    /// <param name="filter">Filters down the instances included in the query.</param>
    /// <returns>An async pageable of the query results.</returns>
    public abstract AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null);

    /// <inheritdoc cref="PurgeInstanceAsync(string, PurgeInstanceOptions, CancellationToken)"/>
    public virtual Task<PurgeResult> PurgeInstanceAsync(string instanceId, CancellationToken cancellation)
        => this.PurgeInstanceAsync(instanceId, null, cancellation);

    /// <summary>
    /// Purges orchestration instance metadata from the durable store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method can be used to permanently delete orchestration metadata from the underlying storage provider,
    /// including any stored inputs, outputs, and orchestration history records. This is often useful for implementing
    /// data retention policies and for keeping storage costs minimal. Only orchestration instances in the
    /// <see cref="OrchestrationRuntimeStatus.Completed"/>, <see cref="OrchestrationRuntimeStatus.Failed"/>, or
    /// <see cref="OrchestrationRuntimeStatus.Terminated"/> state can be purged.
    /// </para><para>
    /// Purging an orchestration will by default not purge any of the child sub-orchestrations that were started by the
    /// orchetration instance. Currently, purging of sub-orchestrations is not supported.
    /// If <paramref name="instanceId"/> is not found in the data store, or if the instance is found but not in a
    /// terminal state, then the returned <see cref="PurgeResult"/> object will have a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> value of <c>0</c>. Otherwise, the existing data will be purged and
    /// <see cref="PurgeResult.PurgedInstanceCount"/> will be the count of purged instances.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The unique ID of the orchestration instance to purge.</param>
    /// <param name="options">The optional options for purging the orchestration.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the purge operation.
    /// </param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged,
    /// including the count of sub-orchestrations purged if any.
    /// </returns>
    public virtual Task<PurgeResult> PurgeInstanceAsync(
        string instanceId, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        throw new NotSupportedException($"{this.GetType()} does not support purging of orchestration instances.");
    }

    /// <inheritdoc cref="PurgeAllInstancesAsync(PurgeInstancesFilter, PurgeInstanceOptions, CancellationToken)"/>
    public virtual Task<PurgeResult> PurgeAllInstancesAsync(PurgeInstancesFilter filter, CancellationToken cancellation)
        => this.PurgeAllInstancesAsync(filter, null, cancellation);

    /// <summary>
    /// Purges orchestration instances metadata from the durable store.
    /// </summary>
    /// <param name="filter">The filter for which orchestrations to purge.</param>
    /// <param name="options">The optional options for purging the orchestration.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the purge operation.
    /// </param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged.
    /// </returns>
    public virtual Task<PurgeResult> PurgeAllInstancesAsync(
        PurgeInstancesFilter filter, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        throw new NotSupportedException($"{this.GetType()} does not support purging of orchestration instances.");
    }

    /// <summary>
    /// Restarts an orchestration instance with the same or a new instance ID.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method restarts an existing orchestration instance. If <paramref name="restartWithNewInstanceId"/> is <c>true</c>,
    /// a new instance ID will be generated for the restarted orchestration. If <c>false</c>, the original instance ID will be reused.
    /// </para><para>
    /// The restarted orchestration will use the same input data as the original instance. If the original orchestration
    /// instance is not found, an <see cref="ArgumentException"/> will be thrown.
    /// </para><para>
    /// Note that this operation is backend-specific and may not be supported by all durable task backends.
    /// If the backend does not support restart operations, a <see cref="NotSupportedException"/> will be thrown.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The ID of the orchestration instance to restart.</param>
    /// <param name="restartWithNewInstanceId">
    /// If <c>true</c>, a new instance ID will be generated for the restarted orchestration.
    /// If <c>false</c>, the original instance ID will be reused.
    /// </param>
    /// <param name="cancellation">
    /// The cancellation token. This only cancels enqueueing the restart request to the backend.
    /// Does not abort restarting the orchestration once enqueued.
    /// </param>
    /// <returns>
    /// A task that completes when the orchestration instance is successfully restarted.
    /// The value of this task is the instance ID of the restarted orchestration instance.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown if an orchestration with the specified <paramref name="instanceId"/> was not found. </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to restart an instance using the same instance Id
    /// while the instance has not yet reached a completed or terminal state. </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown if the backend does not support restart operations. </exception>
    public virtual Task<string> RestartAsync(
        string instanceId,
        bool restartWithNewInstanceId = false,
        CancellationToken cancellation = default)
        => throw new NotSupportedException($"{this.GetType()} does not support orchestration restart.");

    /// <summary>
    /// Rewinds the specified orchestration instance by re-executing any failed Activities, and recursively rewinding
    /// any failed suborchestrations with failed Activities.
    /// </summary>
    /// <remarks>
    /// The orchestration's history will be replaced with a new history that excludes the failed Activities and suborchestrations,
    /// and a new execution ID will be generated for the rewound orchestration instance. As the failed Activities and suborchestrations
    /// re-execute, the history will be appended with new TaskScheduled, TaskCompleted, and SubOrchestrationInstanceCompleted events.
    /// Note that only orchestrations in a "Failed" state can be rewound.
    /// </remarks>
    /// <param name="instanceId">The instance ID of the orchestration to rewind.</param>
    /// <param name="reason">The reason for the rewind.</param>
    /// <param name="cancellation">The cancellation token. This only cancels enqueueing the rewind request to the backend.
    /// It does not abort rewinding the orchestration once the request has been enqueued.</param>
    /// <returns>A task that represents the enqueueing of the rewind operation.</returns>
    /// <exception cref="NotSupportedException">Thrown if this implementation of <see cref="DurableTaskClient"/> does not
    /// support rewinding orchestrations.</exception>
    /// <exception cref="NotImplementedException">Thrown if the backend storage provider does not support rewinding orchestrations.</exception>
    /// <exception cref="ArgumentException">Thrown if an orchestration with the specified <paramref name="instanceId"/> does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a precondition of the operation fails, for example if the specified
    /// orchestration is not in a "Failed" state.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the <paramref name="cancellation"/> token.</exception>
    public virtual Task RewindInstanceAsync(
        string instanceId,
        string reason,
        CancellationToken cancellation = default)
        => throw new NotSupportedException($"{this.GetType()} does not support orchestration rewind.");

    /// <summary>
    /// Lists orchestration instance IDs filtered by completed time.
    /// </summary>
    /// <param name="runtimeStatus">The runtime statuses to filter by.</param>
    /// <param name="completedTimeFrom">The start time for completed time filter (inclusive).</param>
    /// <param name="completedTimeTo">The end time for completed time filter (inclusive).</param>
    /// <param name="pageSize">The page size for pagination.</param>
    /// <param name="lastInstanceKey">The last fetched instance key.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A page of instance IDs with continuation token.</returns>
    public virtual Task<Page<string>> ListInstanceIdsAsync(
        IEnumerable<OrchestrationRuntimeStatus>? runtimeStatus = null,
        DateTimeOffset? completedTimeFrom = null,
        DateTimeOffset? completedTimeTo = null,
        int pageSize = OrchestrationQuery.DefaultPageSize,
        string? lastInstanceKey = null,
        CancellationToken cancellation = default)
    {
        throw new NotSupportedException($"{this.GetType()} does not support listing orchestration instance IDs by completed time.");
    }

    /// <summary>
    /// Streams the execution history events for an orchestration instance.
    /// </summary>
    /// <remarks>
    /// This method returns an async enumerable that yields history events as they are retrieved from the backend.
    /// The history events are returned in chronological order and include all events that occurred during the
    /// orchestration instance's execution.
    /// </remarks>
    /// <param name="instanceId">The instance ID of the orchestration to stream history for.</param>
    /// <param name="executionId">The optional execution ID. If null, the latest execution will be used.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the streaming operation.
    /// </param>
    /// <returns>An async enumerable of <see cref="HistoryEvent"/> objects.</returns>
    /// <exception cref="ArgumentException">Thrown if an orchestration with the specified <paramref name="instanceId"/> does not exist.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the <paramref name="cancellation"/> token.</exception>
    public virtual IAsyncEnumerable<HistoryEvent> StreamInstanceHistoryAsync(
        string instanceId,
        string? executionId = null,
        CancellationToken cancellation = default)

    {
        throw new NotSupportedException($"{this.GetType()} does not support streaming instance history.");
    }

    // TODO: Create task hub

    // TODO: Delete task hub

    /// <summary>
    /// Disposes any unmanaged resources associated with this <see cref="DurableTaskClient"/>.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when the disposal completes.</returns>
    public abstract ValueTask DisposeAsync();
}