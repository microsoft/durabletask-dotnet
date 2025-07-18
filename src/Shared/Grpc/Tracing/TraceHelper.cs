// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using DurableTask.Core.Command;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Methods for starting and managing trace activities related to Durable Task operations.
/// </summary>
/// <remarks>
/// Adapted from "https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/TraceHelper.cs".
/// </remarks>
class TraceHelper
{
    const string Source = "Microsoft.DurableTask";

    static readonly ActivitySource ActivityTraceSource = new ActivitySource(Source);

    /// <summary>
    /// Starts a new trace activity for scheduling an orchestration from the client.
    /// </summary>
    /// <param name="createInstanceRequest">The orchestration's creation request.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with orchestration-specific metadata.
    /// </returns>
    public static Activity? StartActivityForNewOrchestration(P.CreateInstanceRequest createInstanceRequest)
    {
        Activity? newActivity = ActivityTraceSource.StartActivity(
            name: CreateSpanName(TraceActivityConstants.CreateOrchestration, createInstanceRequest.Name, createInstanceRequest.Version),
            kind: ActivityKind.Producer);

        if (newActivity != null)
        {
            newActivity.SetTag(Schema.Task.Type, TraceActivityConstants.Orchestration);
            newActivity.SetTag(Schema.Task.Name, createInstanceRequest.Name);
            newActivity.SetTag(Schema.Task.InstanceId, createInstanceRequest.InstanceId);
            newActivity.SetTag(Schema.Task.ExecutionId, createInstanceRequest.ExecutionId);

            if (!string.IsNullOrEmpty(createInstanceRequest.Version))
            {
                newActivity.SetTag(Schema.Task.Version, createInstanceRequest.Version);
            }
        }

        if (Activity.Current is not null)
        {
            createInstanceRequest.ParentTraceContext ??= new P.TraceContext();
            createInstanceRequest.ParentTraceContext.TraceParent = Activity.Current.Id!;
            createInstanceRequest.ParentTraceContext.TraceState = Activity.Current.TraceStateString;
        }

        return newActivity;
    }

    /// <summary>
    /// Starts a new trace activity for orchestration execution.
    /// </summary>
    /// <param name="startEvent">The orchestration's execution started event.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with orchestration-specific metadata.
    /// </returns>
    public static Activity? StartTraceActivityForOrchestrationExecution(P.ExecutionStartedEvent? startEvent)
    {
        if (startEvent == null)
        {
            return null;
        }

        if (startEvent.ParentTraceContext is null || !ActivityContext.TryParse(startEvent.ParentTraceContext.TraceParent, startEvent.ParentTraceContext.TraceState, out ActivityContext activityContext))
        {
            return null;
        }

        string activityName = CreateSpanName(TraceActivityConstants.Orchestration, startEvent.Name, startEvent.Version);
        ActivityKind activityKind = ActivityKind.Server;
        DateTimeOffset startTime = startEvent.OrchestrationSpanStartTime?.ToDateTimeOffset() ?? default;

        Activity? activity = ActivityTraceSource.StartActivity(
            activityName,
            kind: activityKind,
            parentContext: activityContext,
            startTime: startTime);

        if (activity == null)
        {
            return null;
        }

        activity.SetTag(Schema.Task.Type, TraceActivityConstants.Orchestration);
        activity.SetTag(Schema.Task.Name, startEvent.Name);
        activity.SetTag(Schema.Task.InstanceId, startEvent.OrchestrationInstance.InstanceId);

        if (!string.IsNullOrEmpty(startEvent.Version))
        {
            activity.SetTag(Schema.Task.Version, startEvent.Version);
        }

        if (startEvent.OrchestrationSpanID != null)
        {
            activity.SetSpanId(startEvent.OrchestrationSpanID!);
        }
        else
        {
            startEvent.OrchestrationSpanID = activity.SpanId.ToString();
            startEvent.OrchestrationSpanStartTime = Timestamp.FromDateTime(activity.StartTimeUtc);
        }

        return activity;
    }

    /// <summary>
    /// Starts a new trace activity for (task) activity execution.
    /// </summary>
    /// <param name="request">The associated request to start a (task) activity.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    public static Activity? StartTraceActivityForTaskExecution(
        P.ActivityRequest request)
    {
        if (request.ParentTraceContext is null || !ActivityContext.TryParse(request.ParentTraceContext.TraceParent, request.ParentTraceContext.TraceState, out ActivityContext activityContext))
        {
            return null;
        }

        Activity? newActivity = ActivityTraceSource.StartActivity(
            CreateSpanName(TraceActivityConstants.Activity, request.Name, request.Version),
            kind: ActivityKind.Server,
            parentContext: activityContext);

        if (newActivity == null)
        {
            return null;
        }

        newActivity.SetTag(Schema.Task.Type, TraceActivityConstants.Activity);
        newActivity.SetTag(Schema.Task.Name, request.Name);
        newActivity.SetTag(Schema.Task.InstanceId, request.OrchestrationInstance.InstanceId);
        newActivity.SetTag(Schema.Task.TaskId, request.TaskId);

        if (!string.IsNullOrEmpty(request.Version))
        {
            newActivity.SetTag(Schema.Task.Version, request.Version);
        }

        return newActivity;
    }

    /// <summary>
    /// Emits a new trace activity for a (task) activity that successfully completes.
    /// </summary>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <param name="historyEvent">The associated <see cref="P.HistoryEvent" />.</param>
    /// <param name="taskScheduledEvent">The associated <see cref="P.TaskScheduledEvent"/>.</param>
    public static void EmitTraceActivityForTaskCompleted(
        string? instanceId,
        P.HistoryEvent? historyEvent,
        P.TaskScheduledEvent? taskScheduledEvent)
    {
        // The parent of this is the parent orchestration span ID. It should be the client span which started this
        Activity? activity = StartTraceActivityForSchedulingTask(instanceId, historyEvent, taskScheduledEvent);

        activity?.Dispose();
    }

    /// <summary>
    /// Emits a new trace activity for a (task) activity that fails.
    /// </summary>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <param name="historyEvent">The associated <see cref="P.HistoryEvent" />.</param>
    /// <param name="taskScheduledEvent">The associated <see cref="P.TaskScheduledEvent"/>.</param>
    /// <param name="failedEvent">The associated <see cref="P.TaskFailedEvent"/>.</param>
    public static void EmitTraceActivityForTaskFailed(
        string? instanceId,
        P.HistoryEvent? historyEvent,
        P.TaskScheduledEvent? taskScheduledEvent,
        P.TaskFailedEvent? failedEvent)
    {
        Activity? activity = StartTraceActivityForSchedulingTask(instanceId, historyEvent, taskScheduledEvent);

        if (activity is null)
        {
            return;
        }

        if (failedEvent != null)
        {
            string statusDescription = failedEvent.FailureDetails?.ErrorMessage ?? "Unspecified task activity failure";
            activity?.SetStatus(ActivityStatusCode.Error, statusDescription);
        }

        activity?.Dispose();
    }

    /// <summary>
    /// Emits a new trace activity for sub-orchestration execution when the sub-orchestration
    /// completes successfully.
    /// </summary>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <param name="historyEvent">The associated <see cref="P.HistoryEvent" />.</param>
    /// <param name="createdEvent">The associated <see cref="P.SubOrchestrationInstanceCreatedEvent"/>.</param>
    public static void EmitTraceActivityForSubOrchestrationCompleted(
        string? instanceId,
        P.HistoryEvent? historyEvent,
        P.SubOrchestrationInstanceCreatedEvent? createdEvent)
    {
        // The parent of this is the parent orchestration span ID. It should be the client span which started this
        Activity? activity = CreateTraceActivityForSchedulingSubOrchestration(instanceId, historyEvent, createdEvent);

        activity?.Dispose();
    }

    /// <summary>
    /// Emits a new trace activity for sub-orchestration execution when the sub-orchestration fails.
    /// </summary>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <param name="historyEvent">The associated <see cref="P.HistoryEvent" />.</param>
    /// <param name="createdEvent">The associated <see cref="P.SubOrchestrationInstanceCreatedEvent"/>.</param>
    /// <param name="failedEvent">The associated <see cref="P.SubOrchestrationInstanceFailedEvent"/>.</param>
    public static void EmitTraceActivityForSubOrchestrationFailed(
        string? instanceId,
        P.HistoryEvent? historyEvent,
        P.SubOrchestrationInstanceCreatedEvent? createdEvent,
        P.SubOrchestrationInstanceFailedEvent? failedEvent)
    {
        Activity? activity = CreateTraceActivityForSchedulingSubOrchestration(instanceId, historyEvent, createdEvent);

        if (activity is null)
        {
            return;
        }

        if (failedEvent != null)
        {
            string statusDescription = failedEvent.FailureDetails.ErrorMessage ?? "Unspecified sub-orchestration failure";
            activity?.SetStatus(ActivityStatusCode.Error, statusDescription);
        }

        activity?.Dispose();
    }

    /// <summary>
    /// Emits a new trace activity for events raised from the worker.
    /// </summary>
    /// <param name="eventRaisedEvent">The associated <see cref="SendEventOrchestratorAction"/>.</param>
    /// <param name="instanceId">The instance ID of the associated orchestration.</param>
    /// <param name="executionId">The execution ID of the associated orchestration.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    public static Activity? StartTraceActivityForEventRaisedFromWorker(
        SendEventOrchestratorAction eventRaisedEvent,
        string? instanceId,
        string? executionId)
    {
        Activity? newActivity = ActivityTraceSource.StartActivity(
            CreateSpanName(TraceActivityConstants.OrchestrationEvent, eventRaisedEvent.EventName, null),
            kind: ActivityKind.Producer,
            parentContext: Activity.Current?.Context ?? default);

        if (newActivity == null)
        {
            return null;
        }

        newActivity.AddTag(Schema.Task.Type, TraceActivityConstants.Event);
        newActivity.AddTag(Schema.Task.Name, eventRaisedEvent.EventName);
        newActivity.AddTag(Schema.Task.InstanceId, instanceId);
        newActivity.AddTag(Schema.Task.ExecutionId, executionId);

        if (!string.IsNullOrEmpty(eventRaisedEvent.Instance?.InstanceId))
        {
            newActivity.AddTag(Schema.Task.EventTargetInstanceId, eventRaisedEvent.Instance!.InstanceId);
        }

        return newActivity;
    }

    /// <summary>
    /// Creates a new trace activity for events created from the client.
    /// </summary>
    /// <param name="eventRaised">The associated <see cref="P.EventRaisedEvent"/>.</param>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    public static Activity? StartActivityForNewEventRaisedFromClient(P.RaiseEventRequest eventRaised, string instanceId)
    {
        Activity? newActivity = ActivityTraceSource.StartActivity(
            CreateSpanName(TraceActivityConstants.OrchestrationEvent, eventRaised.Name, null),
            kind: ActivityKind.Producer,
            parentContext: Activity.Current?.Context ?? default,
            tags: new KeyValuePair<string, object?>[]
            {
                new(Schema.Task.Type, TraceActivityConstants.Event),
                new(Schema.Task.Name, eventRaised.Name),
                new(Schema.Task.EventTargetInstanceId, instanceId),
            });

        return newActivity;
    }

    /// <summary>
    /// Emits a new trace activity for timers.
    /// </summary>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <param name="orchestrationName">The name of the orchestration invoking the timer.</param>
    /// <param name="startTime">The timer's start time.</param>
    /// <param name="timerFiredEvent">The associated <see cref="P.TimerFiredEvent"/>.</param>
    public static void EmitTraceActivityForTimer(
        string? instanceId,
        string orchestrationName,
        DateTime startTime,
        P.TimerFiredEvent timerFiredEvent)
    {
        Activity? newActivity = ActivityTraceSource.StartActivity(
            CreateTimerSpanName(orchestrationName),
            kind: ActivityKind.Internal,
            startTime: startTime,
            parentContext: Activity.Current?.Context ?? default);

        if (newActivity is not null)
        {
            newActivity.AddTag(Schema.Task.Type, TraceActivityConstants.Timer);
            newActivity.AddTag(Schema.Task.Name, orchestrationName);
            newActivity.AddTag(Schema.Task.InstanceId, instanceId);
            newActivity.AddTag(Schema.Task.FireAt, timerFiredEvent.FireAt.ToDateTime().ToString("o"));
            newActivity.AddTag(Schema.Task.TaskId, timerFiredEvent.TimerId);

            newActivity.Dispose();
        }
    }

    static string CreateSpanName(string spanDescription, string? taskName, string? taskVersion)
    {
        if (!string.IsNullOrEmpty(taskVersion))
        {
            return $"{spanDescription}:{taskName}@({taskVersion})";
        }
        else
        {
            return $"{spanDescription}:{taskName}";
        }
    }

    /// <summary>
    /// Starts a new trace activity for (task) activity that represents the time between when the task message
    /// is enqueued and when the response message is received.
    /// </summary>
    /// <param name="instanceId">The ID of the associated instance.</param>
    /// <param name="historyEvent">The associated <see cref="P.HistoryEvent" />.</param>
    /// <param name="taskScheduledEvent">The associated <see cref="P.TaskScheduledEvent"/>.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    static Activity? StartTraceActivityForSchedulingTask(
        string? instanceId,
        P.HistoryEvent? historyEvent,
        P.TaskScheduledEvent? taskScheduledEvent)
    {
        if (taskScheduledEvent == null)
        {
            return null;
        }

        Activity? newActivity = ActivityTraceSource.StartActivity(
            CreateSpanName(TraceActivityConstants.Activity, taskScheduledEvent.Name, taskScheduledEvent.Version),
            kind: ActivityKind.Client,
            startTime: historyEvent?.Timestamp?.ToDateTimeOffset() ?? default,
            parentContext: Activity.Current?.Context ?? default);

        if (newActivity == null)
        {
            return null;
        }

        if (taskScheduledEvent.ParentTraceContext != null)
        {
            if (ActivityContext.TryParse(taskScheduledEvent.ParentTraceContext.TraceParent, taskScheduledEvent.ParentTraceContext?.TraceState, out ActivityContext parentContext))
            {
                newActivity.SetSpanId(parentContext.SpanId.ToString());
            }
        }

        newActivity.AddTag(Schema.Task.Type, TraceActivityConstants.Activity);
        newActivity.AddTag(Schema.Task.Name, taskScheduledEvent.Name);
        newActivity.AddTag(Schema.Task.InstanceId, instanceId);
        newActivity.AddTag(Schema.Task.TaskId, historyEvent?.EventId);

        if (!string.IsNullOrEmpty(taskScheduledEvent.Version))
        {
            newActivity.AddTag(Schema.Task.Version, taskScheduledEvent.Version);
        }

        return newActivity;
    }

    /// <summary>
    /// Starts a new trace activity for sub-orchestrations. Represents the time between enqueuing
    /// the sub-orchestration message and it completing.
    /// </summary>
    /// <param name="instanceId">The ID of the associated orchestration.</param>
    /// <param name="historyEvent">The associated <see cref="P.HistoryEvent" />.</param>
    /// <param name="createdEvent">The associated <see cref="P.SubOrchestrationInstanceCreatedEvent"/>.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    static Activity? CreateTraceActivityForSchedulingSubOrchestration(
        string? instanceId,
        P.HistoryEvent? historyEvent,
        P.SubOrchestrationInstanceCreatedEvent? createdEvent)
    {
        if (instanceId == null || createdEvent == null)
        {
            return null;
        }

        Activity? activity = ActivityTraceSource.StartActivity(
            CreateSpanName(TraceActivityConstants.Orchestration, createdEvent.Name, createdEvent.Version),
            kind: ActivityKind.Client,
            startTime: historyEvent?.Timestamp?.ToDateTimeOffset() ?? default,
            parentContext: Activity.Current?.Context ?? default);

        if (activity == null)
        {
            return null;
        }

        if (createdEvent.ParentTraceContext != null && ActivityContext.TryParse(createdEvent.ParentTraceContext.TraceParent, createdEvent.ParentTraceContext.TraceState, out ActivityContext parentContext))
        {
            activity.SetSpanId(parentContext.SpanId.ToString());
        }

        activity.SetTag(Schema.Task.Type, TraceActivityConstants.Orchestration);
        activity.SetTag(Schema.Task.Name, createdEvent.Name);
        activity.SetTag(Schema.Task.InstanceId, instanceId);

        if (!string.IsNullOrEmpty(createdEvent.Version))
        {
            activity.SetTag(Schema.Task.Version, createdEvent.Version);
        }

        return activity;
    }

    static string CreateTimerSpanName(string orchestrationName)
    {
        return $"{TraceActivityConstants.Orchestration}:{orchestrationName}:{TraceActivityConstants.Timer}";
    }
}
