// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
public abstract class DurableTaskClient : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="options">The client options.</param>
    protected DurableTaskClient(string name, DurableTaskClientOptions options)
    {
        this.Name = name;
        this.Options = options;
    }

    /// <summary>
    /// Gets the name of the client.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the common client options.
    /// </summary>
    protected DurableTaskClientOptions Options { get; }

    /// <summary>
    /// Schedules a new orchestration instance for execution.
    /// </summary>
    /// <remarks>
    /// <para>All orchestrations must have a unique instance ID. You can provide an instance ID using the
    /// <paramref name="instanceId"/> parameter or you can omit this parameter and a random instance ID will be
    /// generated for you automatically. If an orchestration with the specified instance ID already exists and is in a
    /// non-terminal state (Pending, Running, etc.), then this operation may fail silently. However, if an orchestration
    /// instance with this ID already exists in a terminal state (Completed, Terminated, Failed, etc.) then the instance
    /// may be recreated automatically, depending on the configuration of the backend instance store.
    /// </para><para>
    /// Orchestration instances started with this method will be created in the
    /// <see cref="OrchestrationRuntimeStatus.Pending"/> state and will transition to the
    /// <see cref="OrchestrationRuntimeStatus.Running"/> after successfully awaiting its first task. The exact time it
    /// takes before a scheduled orchestration starts running depends on several factors, including the configuration
    /// and health of the backend task hub, and whether a <paramref name="startTime"/> value was provided.
    /// </para><para>
    /// The task associated with this method completes after the orchestration instance was successfully scheduled. You
    /// can use the <see cref="GetInstanceMetadataAsync"/> to query the status of the scheduled instance, the
    /// <see cref="WaitForInstanceStartAsync"/> method to wait for the instance to transition out of the
    /// <see cref="OrchestrationRuntimeStatus.Pending"/> status, or the <see cref="WaitForInstanceCompletionAsync"/>
    /// method to wait for the instance to reach a terminal state (Completed, Terminated, Failed, etc.).
    /// </para>
    /// </remarks>
    /// <param name="orchestratorName">The name of the orchestrator to schedule.</param>
    /// <param name="instanceId">
    /// The unique ID of the orchestration instance to schedule. If not specified, a randomGUID value is used.
    /// </param>
    /// <param name="input">
    /// The optional input to pass to the scheduled orchestration instance. This must be a serializable value.
    /// </param>
    /// <param name="startTime">
    /// The time when the orchestration instance should start executing. If not specified or if a date-time in the past
    /// is specified, the orchestration instance will be scheduled immediately.
    /// </param>
    /// <returns>
    /// A task that completes when the orchestration instance is successfully scheduled. The value of this task is
    /// the instance ID of the scheduled orchestration instance. If a non-null <paramref name="instanceId"/> parameter
    /// value was provided, the same value will be returned by the completed task.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="orchestratorName"/> is empty.</exception>
    public abstract Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        string? instanceId = null,
        object? input = null,
        DateTimeOffset? startTime = null);

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
    /// <returns>A task that completes when the event notification message has been enqueued.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="instanceId"/> or <paramref name="eventName"/> is null or empty.
    /// </exception>
    public abstract Task RaiseEventAsync(string instanceId, string eventName, object? eventPayload);

    /// <summary>
    /// Terminates a running orchestration instance and updates its runtime status to
    /// <see cref="OrchestrationRuntimeStatus.Terminated"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method internally enqueues a "terminate" message in the task hub. When the task hub worker processes
    /// this message, it will update the runtime status of the target instance to
    /// <see cref="OrchestrationRuntimeStatus.Terminated"/>. You can use the
    /// <see cref="WaitForInstanceCompletionAsync"/> to wait for the instance to reach the terminated state.
    /// </para>
    /// <para>
    /// Terminating an orchestration instance has no effect on any in-flight activity function executions
    /// or sub-orchestrations that were started by the terminated instance. Those actions will continue to run
    /// without interruption. However, their results will be discarded. If you want to terminate sub-orchestrations,
    /// you must issue separate terminate commands for each sub-orchestration instance.
    /// </para><para>
    /// At the time of writing, there is no way to terminate an in-flight activity execution.
    /// </para><para>
    /// Attempting to terminate a completed or non-existent orchestration instance will fail silently.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The ID of the orchestration instance to terminate.</param>
    /// <param name="output">The optional output to set for the terminated orchestration instance.</param>
    /// <returns>A task that completes when the terminate message is enqueued.</returns>
    public abstract Task TerminateAsync(string instanceId, object? output);

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
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> that can be used to cancel the wait operation.
    /// </param>
    /// <param name="getInputsAndOutputs">
    /// Specify <c>true</c> to fetch the orchestration instance's inputs, outputs, and custom status, or <c>false</c> to
    /// omit them. The default value is <c>false</c> to minimize the network bandwidth, serialization, and memory costs
    /// associated with fetching the instance metadata.
    /// </param>
    /// <returns>
    /// Returns a <see cref="OrchestrationMetadata"/> record that describes the orchestration instance and its execution
    /// status or <c>null</c> if no instance with ID <paramref name="instanceId"/> is found.
    /// </returns>
    public abstract Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId,
        CancellationToken cancellationToken,
        bool getInputsAndOutputs = false);

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
    /// enforced using the <paramref name="cancellationToken"/> parameter.
    /// </para><para>
    /// If an orchestration instance is already complete when this method is called, the method will return immediately.
    /// </para>
    /// </remarks>
    /// <inheritdoc cref="WaitForInstanceStartAsync(string, CancellationToken, bool)"/>
    public abstract Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId,
        CancellationToken cancellationToken,
        bool getInputsAndOutputs = false);

    /// <summary>
    /// Fetches orchestration instance metadata from the configured durable store.
    /// </summary>
    /// <remarks>
    /// You can use the <paramref name="getInputsAndOutputs"/> parameter to determine whether to fetch input and
    /// output data for the target orchestration instance. If your code doesn't require access to this data, it's
    /// recommended that you set this parameter to <c>false</c> to minimize the network bandwidth, serialization, and
    /// memory costs associated with fetching the instance metadata.
    /// </remarks>
    /// <param name="instanceId">The unique ID of the orchestration instance to fetch.</param>
    /// <param name="getInputsAndOutputs">
    /// Specify <c>true</c> to fetch the orchestration instance's inputs, outputs, and custom status, or <c>false</c> to
    /// omit them.
    /// </param>
    /// <inheritdoc cref="WaitForInstanceStartAsync(string, CancellationToken, bool)"/>
    public abstract Task<OrchestrationMetadata?> GetInstanceMetadataAsync(string instanceId, bool getInputsAndOutputs);

    /// <summary>
    /// Queries orchestration instances.
    /// </summary>
    /// <param name="query">Filters down the instances included in the query.</param>
    /// <returns>An async pageable of the query results.</returns>
    public abstract AsyncPageable<OrchestrationMetadata> GetInstances(OrchestrationQuery? query = null);

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
    /// If <paramref name="instanceId"/> is not found in the data store, or if the instance is found but not in a
    /// terminal state, then the returned <see cref="PurgeResult"/> object will have a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> value of <c>0</c>. Otherwise, the existing data will be purged and
    /// <see cref="PurgeResult.PurgedInstanceCount"/> will be <c>1</c>.
    /// </para>
    /// </remarks>
    /// <param name="instanceId">The unique ID of the orchestration instance to purge.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the purge operation.
    /// </param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> value of <c>1</c> or <c>0</c>, depending on whether the target
    /// instance was successfully purged.
    /// </returns>
    public abstract Task<PurgeResult> PurgeInstanceMetadataAsync(
        string instanceId, CancellationToken cancellation = default);

    /// <summary>
    /// Purges orchestration instances metadata from the durable store.
    /// </summary>
    /// <param name="filter">The filter for which orchestrations to purge.</param>
    /// <param name="cancellation">
    /// A <see cref="CancellationToken"/> that can be used to cancel the purge operation.
    /// </param>
    /// <returns>
    /// This method returns a <see cref="PurgeResult"/> object after the operation has completed with a
    /// <see cref="PurgeResult.PurgedInstanceCount"/> indicating the number of orchestration instances that were purged.
    /// </returns>
    public abstract Task<PurgeResult> PurgeInstancesAsync(
        PurgeInstancesFilter filter, CancellationToken cancellation = default);

    // TODO: Create task hub

    // TODO: Delete task hub

    /// <summary>
    /// Disposes any unmanaged resources associated with this <see cref="DurableTaskClient"/>.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when the disposal completes.</returns>
    public abstract ValueTask DisposeAsync();
}
