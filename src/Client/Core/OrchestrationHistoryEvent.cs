// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Represents a single event in an orchestration instance's history.
/// </summary>
/// <remarks>
/// <para>
/// History events capture the sequence of actions taken during an orchestration's execution,
/// including task scheduling, completions, timer creation, sub-orchestration invocations, and more.
/// </para>
/// <para>
/// This record provides a strongly-typed representation of history events that were previously
/// only available through the in-process Durable Functions SDK.
/// </para>
/// </remarks>
/// <param name="EventId">The unique identifier for this event within the orchestration history.</param>
/// <param name="EventType">The type of the history event.</param>
/// <param name="Timestamp">The timestamp when this event occurred.</param>
public sealed record OrchestrationHistoryEvent(int EventId, string EventType, DateTimeOffset Timestamp)
{
    /// <summary>
    /// Gets the name associated with the event, if applicable.
    /// </summary>
    /// <remarks>
    /// For <c>TaskScheduled</c> events, this is the activity name.
    /// For <c>SubOrchestrationInstanceCreated</c> events, this is the sub-orchestration name.
    /// For <c>EventRaised</c> and <c>EventSent</c> events, this is the event name.
    /// </remarks>
    /// <value>The name associated with the event, or <c>null</c> if not applicable.</value>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the scheduled task ID for events that reference a previously scheduled task.
    /// </summary>
    /// <remarks>
    /// This property is populated for <c>TaskCompleted</c>, <c>TaskFailed</c>,
    /// <c>SubOrchestrationInstanceCompleted</c>, <c>SubOrchestrationInstanceFailed</c>,
    /// and <c>TimerFired</c> events to correlate with the original scheduled event.
    /// </remarks>
    /// <value>The scheduled task ID, or <c>null</c> if not applicable.</value>
    public int? ScheduledTaskId { get; init; }

    /// <summary>
    /// Gets the serialized input data associated with the event, if any.
    /// </summary>
    /// <value>The serialized input data, or <c>null</c> if not applicable.</value>
    public string? Input { get; init; }

    /// <summary>
    /// Gets the serialized result data associated with the event, if any.
    /// </summary>
    /// <value>The serialized result data, or <c>null</c> if not applicable.</value>
    public string? Result { get; init; }

    /// <summary>
    /// Gets the orchestration status for <c>ExecutionCompleted</c> events.
    /// </summary>
    /// <value>The orchestration status, or <c>null</c> if not applicable.</value>
    public OrchestrationRuntimeStatus? OrchestrationStatus { get; init; }

    /// <summary>
    /// Gets the failure details for failed events.
    /// </summary>
    /// <value>The failure details, or <c>null</c> if the event did not represent a failure.</value>
    public TaskFailureDetails? FailureDetails { get; init; }

    /// <summary>
    /// Gets the timer fire time for timer-related events.
    /// </summary>
    /// <value>The scheduled fire time, or <c>null</c> if not applicable.</value>
    public DateTimeOffset? FireAt { get; init; }

    /// <summary>
    /// Gets the instance ID for sub-orchestration or event-related events.
    /// </summary>
    /// <value>The target instance ID, or <c>null</c> if not applicable.</value>
    public string? InstanceId { get; init; }
}
