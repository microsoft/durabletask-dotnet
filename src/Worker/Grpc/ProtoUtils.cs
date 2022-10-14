﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Protobuf utilities and helpers.
/// </summary>
static class ProtoUtils
{
    /// <summary>
    /// Converts a history event from <see cref="P.HistoryEvent" /> to <see cref="HistoryEvent" />.
    /// </summary>
    /// <param name="proto">The proto history event to converter.</param>
    /// <returns>The converted history event.</returns>
    /// <exception cref="NotSupportedException">When the provided history event type is not supported.</exception>
    internal static HistoryEvent ConvertHistoryEvent(P.HistoryEvent proto)
    {
        HistoryEvent historyEvent;
        switch (proto.EventTypeCase)
        {
            case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew:
                historyEvent = new ContinueAsNewEvent(proto.EventId, proto.ContinueAsNew.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted:
                historyEvent = new ExecutionStartedEvent(proto.EventId, proto.ExecutionStarted.Input)
                {
                    Name = proto.ExecutionStarted.Name,
                    Version = proto.ExecutionStarted.Version,
                    OrchestrationInstance = proto.ExecutionStarted.OrchestrationInstance.ToCore(),
                    ParentInstance = proto.ExecutionStarted.ParentInstance == null ? null : new ParentInstance
                    {
                        Name = proto.ExecutionStarted.ParentInstance.Name,
                        Version = proto.ExecutionStarted.ParentInstance.Version,
                        OrchestrationInstance = proto.ExecutionStarted.ParentInstance.OrchestrationInstance.ToCore(),
                        TaskScheduleId = proto.ExecutionStarted.ParentInstance.TaskScheduledId,
                    },
                    Correlation = proto.ExecutionStarted.CorrelationData,
                    ScheduledStartTime = proto.ExecutionStarted.ScheduledStartTimestamp?.ToDateTime(),
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted:
                historyEvent = new ExecutionCompletedEvent(
                    proto.EventId,
                    proto.ExecutionCompleted.Result,
                    proto.ExecutionCompleted.OrchestrationStatus.ToCore());
                break;
            case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated:
                historyEvent = new ExecutionTerminatedEvent(proto.EventId, proto.ExecutionTerminated.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskScheduled:
                historyEvent = new TaskScheduledEvent(
                    proto.EventId,
                    proto.TaskScheduled.Name,
                    proto.TaskScheduled.Version,
                    proto.TaskScheduled.Input);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskCompleted:
                historyEvent = new TaskCompletedEvent(
                    proto.EventId,
                    proto.TaskCompleted.TaskScheduledId,
                    proto.TaskCompleted.Result);
                break;
            case P.HistoryEvent.EventTypeOneofCase.TaskFailed:
                historyEvent = new TaskFailedEvent(
                    proto.EventId,
                    proto.TaskFailed.TaskScheduledId,
                    reason: null,  /* not supported */
                    details: null, /* not supported */
                    proto.TaskFailed.FailureDetails.ToCore());
                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated:
                historyEvent = new SubOrchestrationInstanceCreatedEvent(proto.EventId)
                {
                    Input = proto.SubOrchestrationInstanceCreated.Input,
                    InstanceId = proto.SubOrchestrationInstanceCreated.InstanceId,
                    Name = proto.SubOrchestrationInstanceCreated.Name,
                    Version = proto.SubOrchestrationInstanceCreated.Version,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted:
                historyEvent = new SubOrchestrationInstanceCompletedEvent(
                    proto.EventId,
                    proto.SubOrchestrationInstanceCompleted.TaskScheduledId,
                    proto.SubOrchestrationInstanceCompleted.Result);
                break;
            case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed:
                historyEvent = new SubOrchestrationInstanceFailedEvent(
                    proto.EventId,
                    proto.SubOrchestrationInstanceFailed.TaskScheduledId,
                    reason: null /* not supported */,
                    details: null /* not supported */,
                    proto.SubOrchestrationInstanceFailed.FailureDetails.ToCore());
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerCreated:
                historyEvent = new TimerCreatedEvent(
                    proto.EventId,
                    proto.TimerCreated.FireAt.ToDateTime());
                break;
            case P.HistoryEvent.EventTypeOneofCase.TimerFired:
                historyEvent = new TimerFiredEvent(
                    eventId: -1,
                    proto.TimerFired.FireAt.ToDateTime())
                {
                    TimerId = proto.TimerFired.TimerId,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.OrchestratorStarted:
                historyEvent = new OrchestratorStartedEvent(proto.EventId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.OrchestratorCompleted:
                historyEvent = new OrchestratorCompletedEvent(proto.EventId);
                break;
            case P.HistoryEvent.EventTypeOneofCase.EventSent:
                historyEvent = new EventSentEvent(proto.EventId)
                {
                    InstanceId = proto.EventSent.InstanceId,
                    Name = proto.EventSent.Name,
                    Input = proto.EventSent.Input,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.EventRaised:
                historyEvent = new EventRaisedEvent(proto.EventId, proto.EventRaised.Input)
                {
                    Name = proto.EventRaised.Name,
                };
                break;
            case P.HistoryEvent.EventTypeOneofCase.GenericEvent:
                historyEvent = new GenericEvent(proto.EventId, proto.GenericEvent.Data);
                break;
            case P.HistoryEvent.EventTypeOneofCase.HistoryState:
                historyEvent = new HistoryStateEvent(
                    proto.EventId,
                    new OrchestrationState
                    {
                        OrchestrationInstance = new OrchestrationInstance
                        {
                            InstanceId = proto.HistoryState.OrchestrationState.InstanceId,
                        },
                        Name = proto.HistoryState.OrchestrationState.Name,
                        Version = proto.HistoryState.OrchestrationState.Version,
                        ScheduledStartTime = proto.HistoryState.OrchestrationState.ScheduledStartTimestamp.ToDateTime(),
                        CreatedTime = proto.HistoryState.OrchestrationState.CreatedTimestamp.ToDateTime(),
                        LastUpdatedTime = proto.HistoryState.OrchestrationState.LastUpdatedTimestamp.ToDateTime(),
                        Input = proto.HistoryState.OrchestrationState.Input,
                        Output = proto.HistoryState.OrchestrationState.Output,
                        Status = proto.HistoryState.OrchestrationState.CustomStatus,
                    });
                break;
            default:
                throw new NotSupportedException($"Deserialization of {proto.EventTypeCase} is not supported.");
        }

        historyEvent.Timestamp = proto.Timestamp.ToDateTime();
        return historyEvent;
    }

    /// <summary>
    /// Converts a <see cref="DateTime" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp ToTimestamp(this DateTime dateTime)
    {
        // The protobuf libraries require timestamps to be in UTC
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }
        else if (dateTime.Kind == DateTimeKind.Local)
        {
            dateTime = dateTime.ToUniversalTime();
        }

        return Timestamp.FromDateTime(dateTime);
    }

    /// <summary>
    /// Converts a <see cref="DateTime" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp? ToTimestamp(this DateTime? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

    /// <summary>
    /// Converts a <see cref="DateTimeOffset" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp ToTimestamp(this DateTimeOffset dateTime) => Timestamp.FromDateTimeOffset(dateTime);

    /// <summary>
    /// Converts a <see cref="DateTimeOffset" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp? ToTimestamp(this DateTimeOffset? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

    /// <summary>
    /// Constructs a <see cref="P.OrchestratorResponse" />.
    /// </summary>
    /// <param name="instanceId">The orchestrator instance ID.</param>
    /// <param name="customStatus">The orchestrator customer status or <c>null</c> if no custom status.</param>
    /// <param name="actions">The orchestrator actions.</param>
    /// <returns>The orchestrator response.</returns>
    /// <exception cref="NotSupportedException">When an orchestrator action is unknown.</exception>
    internal static P.OrchestratorResponse ConstructOrchestratorResponse(
        string instanceId,
        string? customStatus,
        IEnumerable<OrchestratorAction> actions)
    {
        var response = new P.OrchestratorResponse
        {
            InstanceId = instanceId,
            CustomStatus = customStatus,
        };

        foreach (OrchestratorAction action in actions)
        {
            var protoAction = new P.OrchestratorAction { Id = action.Id };

            switch (action.OrchestratorActionType)
            {
                case OrchestratorActionType.ScheduleOrchestrator:
                    var scheduleTaskAction = (ScheduleTaskOrchestratorAction)action;
                    protoAction.ScheduleTask = new P.ScheduleTaskAction
                    {
                        Name = scheduleTaskAction.Name,
                        Version = scheduleTaskAction.Version,
                        Input = scheduleTaskAction.Input,
                    };
                    break;
                case OrchestratorActionType.CreateSubOrchestration:
                    var subOrchestrationAction = (CreateSubOrchestrationAction)action;
                    protoAction.CreateSubOrchestration = new P.CreateSubOrchestrationAction
                    {
                        Input = subOrchestrationAction.Input,
                        InstanceId = subOrchestrationAction.InstanceId,
                        Name = subOrchestrationAction.Name,
                        Version = subOrchestrationAction.Version,
                    };
                    break;
                case OrchestratorActionType.CreateTimer:
                    var createTimerAction = (CreateTimerOrchestratorAction)action;
                    protoAction.CreateTimer = new P.CreateTimerAction
                    {
                        FireAt = createTimerAction.FireAt.ToTimestamp(),
                    };
                    break;
                case OrchestratorActionType.SendEvent:
                    var sendEventAction = (SendEventOrchestratorAction)action;
                    if (sendEventAction.Instance == null)
                    {
                        throw new ArgumentException(
                            $"{nameof(SendEventOrchestratorAction)} cannot have a null Instance property!");
                    }

                    protoAction.SendEvent = new P.SendEventAction
                    {
                        Instance = sendEventAction.Instance.ToProtobuf(),
                        Name = sendEventAction.EventName,
                        Data = sendEventAction.EventData,
                    };
                    break;
                case OrchestratorActionType.OrchestrationComplete:
                    var completeAction = (OrchestrationCompleteOrchestratorAction)action;
                    protoAction.CompleteOrchestration = new P.CompleteOrchestrationAction
                    {
                        CarryoverEvents =
                        {
                            // TODO
                        },
                        Details = completeAction.Details,
                        NewVersion = completeAction.NewVersion,
                        OrchestrationStatus = completeAction.OrchestrationStatus.ToProtobuf(),
                        Result = completeAction.Result,
                    };

                    if (completeAction.OrchestrationStatus == OrchestrationStatus.Failed)
                    {
                        protoAction.CompleteOrchestration.FailureDetails = completeAction.FailureDetails.ToProtobuf();
                    }

                    break;
                default:
                    throw new NotSupportedException($"Unknown orchestrator action: {action.OrchestratorActionType}");
            }

            response.Actions.Add(protoAction);
        }

        return response;
    }

    /// <summary>
    /// Converts a <see cref="P.OrchestrationStatus" /> to a <see cref="OrchestrationStatus" />.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The converted status.</returns>
    internal static OrchestrationStatus ToCore(this P.OrchestrationStatus status)
    {
        return (OrchestrationStatus)status;
    }

    /// <summary>
    /// Converts a <see cref="P.OrchestrationStatus" /> to a <see cref="OrchestrationStatus" />.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The converted status.</returns>
    [return: NotNullIfNotNull("status")]
    internal static OrchestrationInstance? ToCore(this P.OrchestrationInstance? status)
    {
        if (status == null)
        {
            return null;
        }

        return new OrchestrationInstance
        {
            InstanceId = status.InstanceId,
            ExecutionId = status.ExecutionId,
        };
    }

    /// <summary>
    /// Converts a <see cref="Exception" /> to <see cref="P.TaskFailureDetails" />.
    /// </summary>
    /// <param name="e">The exception to convert.</param>
    /// <returns>The task failure details.</returns>
    internal static P.TaskFailureDetails? ToTaskFailureDetails(this Exception? e)
    {
        if (e == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = e.GetType().FullName,
            ErrorMessage = e.Message,
            StackTrace = e.StackTrace,
            InnerFailure = e.InnerException.ToTaskFailureDetails(),
        };
    }

    /// <summary>
    /// Gets the approximate byte count for a <see cref="P.TaskFailureDetails" />.
    /// </summary>
    /// <param name="failureDetails">The failure details.</param>
    /// <returns>The approximate byte count.</returns>
    internal static int GetApproximateByteCount(this P.TaskFailureDetails failureDetails)
    {
        // Protobuf strings are always UTF-8: https://developers.google.com/protocol-buffers/docs/proto3#scalar
        Encoding encoding = Encoding.UTF8;

        int byteCount = 0;
        if (failureDetails.ErrorType != null)
        {
            byteCount += encoding.GetByteCount(failureDetails.ErrorType);
        }

        if (failureDetails.ErrorMessage != null)
        {
            byteCount += encoding.GetByteCount(failureDetails.ErrorMessage);
        }

        if (failureDetails.StackTrace != null)
        {
            byteCount += encoding.GetByteCount(failureDetails.StackTrace);
        }

        if (failureDetails.InnerFailure != null)
        {
            byteCount += failureDetails.InnerFailure.GetApproximateByteCount();
        }

        return byteCount;
    }

    /// <summary>
    /// Decode a protobuf message from a base64 string.
    /// </summary>
    /// <typeparam name="T">The type to decode to.</typeparam>
    /// <param name="parser">The message parser.</param>
    /// <param name="encodedMessage">The base64 encoded message.</param>
    /// <returns>The decoded message.</returns>
    /// <exception cref="ArgumentException">If decoding fails.</exception>
    internal static T Base64Decode<T>(this MessageParser parser, string encodedMessage) where T : IMessage
    {
        // Decode the base64 in a way that doesn't allocate a byte[] on each request
        int encodedByteCount = Encoding.UTF8.GetByteCount(encodedMessage);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(encodedByteCount);
        try
        {
            // The Base64 APIs require first converting the string into UTF-8 bytes. We then
            // do an in-place conversion from base64 UTF-8 bytes to protobuf bytes so that
            // we can finally decode the protobuf request.
            Encoding.UTF8.GetBytes(encodedMessage, 0, encodedMessage.Length, buffer, 0);
            OperationStatus status = Base64.DecodeFromUtf8InPlace(
                buffer.AsSpan(0, encodedByteCount),
                out int bytesWritten);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    $"Failed to base64-decode the '{typeof(T).Name}' payload: {status}", nameof(encodedMessage));
            }

            return (T)parser.ParseFrom(buffer, 0, bytesWritten);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    static P.OrchestrationStatus ToProtobuf(this OrchestrationStatus status)
    {
        return (P.OrchestrationStatus)status;
    }

    static P.OrchestrationInstance ToProtobuf(this OrchestrationInstance instance)
    {
        return new P.OrchestrationInstance
        {
            InstanceId = instance.InstanceId,
            ExecutionId = instance.ExecutionId,
        };
    }

    static FailureDetails? ToCore(this P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new FailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            failureDetails.InnerFailure.ToCore(),
            failureDetails.IsNonRetriable);
    }

    static P.TaskFailureDetails? ToProtobuf(this FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType ?? "(unknown)",
            ErrorMessage = failureDetails.ErrorMessage ?? "(unknown)",
            StackTrace = failureDetails.StackTrace,
            IsNonRetriable = failureDetails.IsNonRetriable,
            InnerFailure = failureDetails.InnerFailure.ToProtobuf(),
        };
    }
}
