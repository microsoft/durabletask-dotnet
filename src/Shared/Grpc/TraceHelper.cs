// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

public class TraceHelper
{
    const string Source = "Microsoft.DurableTask";

    static readonly ActivitySource ActivityTraceSource = new ActivitySource(Source);

    /// <summary>
    /// Starts a new trace activity for scheduling an orchestration from the client.
    /// </summary>
    /// <param name="startEvent">The orchestration's execution started event.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with orchestration-specific metadata.
    /// </returns>
    internal static Activity? StartActivityForNewOrchestration(P.CreateInstanceRequest createInstanceRequest)
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

        if (Activity.Current?.Id != null || Activity.Current?.TraceStateString != null)
        {
            createInstanceRequest.ParentTraceContext ??= new P.TraceContext();

            if (Activity.Current?.Id != null)
            {
                createInstanceRequest.ParentTraceContext.TraceParent = Activity.Current?.Id;
            }

            if (Activity.Current?.TraceStateString != null)
            {
                createInstanceRequest.ParentTraceContext.TraceState = Activity.Current?.TraceStateString;
            }
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
    internal static Activity? StartTraceActivityForOrchestrationExecution(P.ExecutionStartedEvent? startEvent)
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
        DateTimeOffset startTime = startEvent.OrchestrationActivityStartTime?.ToDateTimeOffset() ?? default;

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

        if (startEvent.OrchestrationActivityID != null && startEvent.OrchestrationSpanID != null)
        {
            activity.SetId(startEvent.OrchestrationActivityID!);
            activity.SetSpanId(startEvent.OrchestrationSpanID!);
        }
        else
        {
            startEvent.OrchestrationActivityID = activity.Id;
            startEvent.OrchestrationSpanID = activity.SpanId.ToString();
            startEvent.OrchestrationActivityStartTime = Timestamp.FromDateTime(activity.StartTimeUtc);
        }

        // DistributedTraceActivity.Current = activity;
        // return DistributedTraceActivity.Current;

        return activity;
    }

    /// <summary>
    /// Starts a new trace activity for (task) activity execution. 
    /// </summary>
    /// <param name="scheduledEvent">The associated <see cref="TaskScheduledEvent"/>.</param>
    /// <param name="instance">The associated orchestration instance metadata.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    internal static Activity? StartTraceActivityForTaskExecution(
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
    /// Starts a new trace activity for (task) activity that represents the time between when the task message
    /// is enqueued and when the response message is received.
    /// </summary>
    /// <param name="instance">The associated <see cref="OrchestrationInstance"/>.</param>
    /// <param name="taskScheduledEvent">The associated <see cref="TaskScheduledEvent"/>.</param>
    /// <returns>
    /// Returns a newly started <see cref="Activity"/> with (task) activity and orchestration-specific metadata.
    /// </returns>
    internal static Activity? StartTraceActivityForSchedulingTask(
        string? instanceId,
        P.HistoryEvent historyEvent,
        P.TaskScheduledEvent taskScheduledEvent)
    {
        if (taskScheduledEvent == null)
        {
            return null;
        }

        Activity? newActivity = ActivityTraceSource.StartActivity(
            CreateSpanName(TraceActivityConstants.Activity, taskScheduledEvent.Name, taskScheduledEvent.Version),
            kind: ActivityKind.Client,
            startTime: historyEvent.Timestamp?.ToDateTimeOffset() ?? default,
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
        newActivity.AddTag(Schema.Task.TaskId, historyEvent.EventId);

        if (!string.IsNullOrEmpty(taskScheduledEvent.Version))
        {
            newActivity.AddTag(Schema.Task.Version, taskScheduledEvent.Version);
        }

        return newActivity;
    }

    /// <summary>
    /// Emits a new trace activity for a (task) activity that successfully completes.
    /// </summary>
    /// <param name="taskScheduledEvent">The associated <see cref="TaskScheduledEvent"/>.</param>
    /// <param name="orchestrationInstance">The associated <see cref="OrchestrationInstance"/>.</param>
    internal static void EmitTraceActivityForTaskCompleted(
        string? instanceId,
        P.HistoryEvent historyEvent,
        P.TaskScheduledEvent taskScheduledEvent)
    {
        // The parent of this is the parent orchestration span ID. It should be the client span which started this
        Activity? activity = StartTraceActivityForSchedulingTask(instanceId, historyEvent, taskScheduledEvent);

        activity?.Dispose();
    }

    /// <summary>
    /// Emits a new trace activity for a (task) activity that fails.
    /// </summary>
    /// <param name="orchestrationInstance">The associated <see cref="OrchestrationInstance"/>.</param>
    /// <param name="taskScheduledEvent">The associated <see cref="TaskScheduledEvent"/>.</param>
    /// <param name="failedEvent">The associated <see cref="TaskFailedEvent"/>.</param>
    /// <param name="errorPropagationMode">Specifies the method to propagate unhandled exceptions to parent orchestrations.</param>
    internal static void EmitTraceActivityForTaskFailed(
        string? instanceId,
        P.HistoryEvent historyEvent,
        P.TaskScheduledEvent taskScheduledEvent,
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
}
