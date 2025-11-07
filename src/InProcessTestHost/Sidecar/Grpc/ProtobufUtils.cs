// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using DurableTask.Core.Tracing;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Testing.Sidecar.Dispatcher;
using Proto = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Testing.Sidecar.Grpc;

/// <summary>
/// Protobuf utils for the in-process grpc service.
/// </summary>
public static class ProtobufUtils
{
    /// <summary>
    /// Convert HistoryEvent to Microsoft.DurableTask.Protobuf.HistoryEvent.
    /// </summary>
    /// <param name="e">The event to convert.</param>
    /// <returns>Microsoft.DurableTask.Protobuf.HistoryEvent of ths passed event.</returns>
    /// <exception cref="NotSupportedException">Throw if the provided event is not supported.</exception>
    public static Proto.HistoryEvent ToHistoryEventProto(HistoryEvent e)
    {
        var payload = new Proto.HistoryEvent()
        {
            EventId = e.EventId,
            Timestamp = Timestamp.FromDateTime(e.Timestamp),
        };

        switch (e.EventType)
        {
            case EventType.ContinueAsNew:
                var continueAsNew = (ContinueAsNewEvent)e;
                payload.ContinueAsNew = new Proto.ContinueAsNewEvent
                {
                    Input = continueAsNew.Result,
                };
                break;
            case EventType.EventRaised:
                var eventRaised = (EventRaisedEvent)e;
                payload.EventRaised = new Proto.EventRaisedEvent
                {
                    Name = eventRaised.Name,
                    Input = eventRaised.Input,
                };
                break;
            case EventType.EventSent:
                var eventSent = (EventSentEvent)e;
                payload.EventSent = new Proto.EventSentEvent
                {
                    Name = eventSent.Name,
                    Input = eventSent.Input,
                    InstanceId = eventSent.InstanceId,
                };
                break;
            case EventType.ExecutionCompleted:
                var completedEvent = (ExecutionCompletedEvent)e;
                payload.ExecutionCompleted = new Proto.ExecutionCompletedEvent
                {
                    OrchestrationStatus = Proto.OrchestrationStatus.Completed,
                    Result = completedEvent.Result,
                };
                break;
            case EventType.ExecutionFailed:
                var failedEvent = (ExecutionCompletedEvent)e;
                payload.ExecutionCompleted = new Proto.ExecutionCompletedEvent
                {
                    OrchestrationStatus = Proto.OrchestrationStatus.Failed,
                    Result = failedEvent.Result,
                };
                break;
            case EventType.ExecutionStarted:
                // Start of a new orchestration instance
                var startedEvent = (ExecutionStartedEvent)e;
                startedEvent.Tags ??= new Dictionary<string, string>();
                payload.ExecutionStarted = new Proto.ExecutionStartedEvent
                {
                    Name = startedEvent.Name,
                    Version = startedEvent.Version,
                    Input = startedEvent.Input,
                    Tags = { startedEvent.Tags },
                    OrchestrationInstance = new Proto.OrchestrationInstance
                    {
                        InstanceId = startedEvent.OrchestrationInstance.InstanceId,
                        ExecutionId = startedEvent.OrchestrationInstance.ExecutionId,
                    },
                    ParentInstance = startedEvent.ParentInstance == null ? null : new Proto.ParentInstanceInfo
                    {
                        Name = startedEvent.ParentInstance.Name,
                        Version = startedEvent.ParentInstance.Version,
                        TaskScheduledId = startedEvent.ParentInstance.TaskScheduleId,
                        OrchestrationInstance = new Proto.OrchestrationInstance
                        {
                            InstanceId = startedEvent.ParentInstance.OrchestrationInstance.InstanceId,
                            ExecutionId = startedEvent.ParentInstance.OrchestrationInstance.ExecutionId,
                        },
                    },
                    ScheduledStartTimestamp = startedEvent.ScheduledStartTime == null ? null : Timestamp.FromDateTime(startedEvent.ScheduledStartTime.Value),
                    ParentTraceContext = startedEvent.ParentTraceContext is null ? null : new Proto.TraceContext
                    {
                        TraceParent = startedEvent.ParentTraceContext.TraceParent,
                        TraceState = startedEvent.ParentTraceContext.TraceState,
                    },
                };
                break;
            case EventType.ExecutionTerminated:
                var terminatedEvent = (ExecutionTerminatedEvent)e;
                payload.ExecutionTerminated = new Proto.ExecutionTerminatedEvent
                {
                    Input = terminatedEvent.Input,
                };
                break;
            case EventType.TaskScheduled:
                var taskScheduledEvent = (TaskScheduledEvent)e;
                payload.TaskScheduled = new Proto.TaskScheduledEvent
                {
                    Name = taskScheduledEvent.Name,
                    Version = taskScheduledEvent.Version,
                    Input = taskScheduledEvent.Input,
                    ParentTraceContext = taskScheduledEvent.ParentTraceContext is null ? null : new Proto.TraceContext
                    {
                        TraceParent = taskScheduledEvent.ParentTraceContext.TraceParent,
                        TraceState = taskScheduledEvent.ParentTraceContext.TraceState,
                    },
                };
                break;
            case EventType.TaskCompleted:
                var taskCompletedEvent = (TaskCompletedEvent)e;
                payload.TaskCompleted = new Proto.TaskCompletedEvent
                {
                    Result = taskCompletedEvent.Result,
                    TaskScheduledId = taskCompletedEvent.TaskScheduledId,
                };
                break;
            case EventType.TaskFailed:
                var taskFailedEvent = (TaskFailedEvent)e;
                payload.TaskFailed = new Proto.TaskFailedEvent
                {
                    FailureDetails = GetFailureDetails(taskFailedEvent.FailureDetails),
                    TaskScheduledId = taskFailedEvent.TaskScheduledId,
                };
                break;
            case EventType.SubOrchestrationInstanceCreated:
                var subOrchestrationCreated = (SubOrchestrationInstanceCreatedEvent)e;
                payload.SubOrchestrationInstanceCreated = new Proto.SubOrchestrationInstanceCreatedEvent
                {
                    Input = subOrchestrationCreated.Input,
                    InstanceId = subOrchestrationCreated.InstanceId,
                    Name = subOrchestrationCreated.Name,
                    Version = subOrchestrationCreated.Version,
                };

                if (subOrchestrationCreated is GrpcSubOrchestrationInstanceCreatedEvent { ParentTraceContext: not null } grpcEvent)
                {
                    payload.SubOrchestrationInstanceCreated.ParentTraceContext = new Proto.TraceContext
                    {
                        TraceParent = grpcEvent.ParentTraceContext.TraceParent,
                        TraceState = grpcEvent.ParentTraceContext.TraceState,
                    };
                }

                break;
            case EventType.SubOrchestrationInstanceCompleted:
                var subOrchestrationCompleted = (SubOrchestrationInstanceCompletedEvent)e;
                payload.SubOrchestrationInstanceCompleted = new Proto.SubOrchestrationInstanceCompletedEvent
                {
                    Result = subOrchestrationCompleted.Result,
                    TaskScheduledId = subOrchestrationCompleted.TaskScheduledId,
                };
                break;
            case EventType.SubOrchestrationInstanceFailed:
                var subOrchestrationFailed = (SubOrchestrationInstanceFailedEvent)e;
                payload.SubOrchestrationInstanceFailed = new Proto.SubOrchestrationInstanceFailedEvent
                {
                    FailureDetails = GetFailureDetails(subOrchestrationFailed.FailureDetails),
                    TaskScheduledId = subOrchestrationFailed.TaskScheduledId,
                };
                break;
            case EventType.TimerCreated:
                var timerCreatedEvent = (TimerCreatedEvent)e;
                payload.TimerCreated = new Proto.TimerCreatedEvent
                {
                    FireAt = Timestamp.FromDateTime(timerCreatedEvent.FireAt),
                };
                break;
            case EventType.TimerFired:
                var timerFiredEvent = (TimerFiredEvent)e;
                payload.TimerFired = new Proto.TimerFiredEvent
                {
                    FireAt = Timestamp.FromDateTime(timerFiredEvent.FireAt),
                    TimerId = timerFiredEvent.TimerId,
                };
                break;
            case EventType.OrchestratorStarted:
                // This event has no data
                payload.OrchestratorStarted = new Proto.OrchestratorStartedEvent();
                break;
            case EventType.OrchestratorCompleted:
                // This event has no data
                payload.OrchestratorCompleted = new Proto.OrchestratorCompletedEvent();
                break;
            case EventType.GenericEvent:
                var genericEvent = (GenericEvent)e;
                payload.GenericEvent = new Proto.GenericEvent
                {
                    Data = genericEvent.Data,
                };
                break;
            case EventType.HistoryState:
                var historyStateEvent = (HistoryStateEvent)e;
                payload.HistoryState = new Proto.HistoryStateEvent
                {
                    OrchestrationState = new Proto.OrchestrationState
                    {
                        InstanceId = historyStateEvent.State.OrchestrationInstance.InstanceId,
                        Name = historyStateEvent.State.Name,
                        Version = historyStateEvent.State.Version,
                        Input = historyStateEvent.State.Input,
                        Output = historyStateEvent.State.Output,
                        ScheduledStartTimestamp = historyStateEvent.State.ScheduledStartTime == null ? null : Timestamp.FromDateTime(historyStateEvent.State.ScheduledStartTime.Value),
                        CreatedTimestamp = Timestamp.FromDateTime(historyStateEvent.State.CreatedTime),
                        LastUpdatedTimestamp = Timestamp.FromDateTime(historyStateEvent.State.LastUpdatedTime),
                        OrchestrationStatus = (Proto.OrchestrationStatus)historyStateEvent.State.OrchestrationStatus,
                        CustomStatus = historyStateEvent.State.Status,
                        Tags = { historyStateEvent.State.Tags },
                    },
                };
                break;
            case EventType.ExecutionSuspended:
                var suspendedEvent = (ExecutionSuspendedEvent)e;
                payload.ExecutionSuspended = new Proto.ExecutionSuspendedEvent
                {
                    Input = suspendedEvent.Reason,
                };
                break;
            case EventType.ExecutionResumed:
                var resumedEvent = (ExecutionResumedEvent)e;
                payload.ExecutionResumed = new Proto.ExecutionResumedEvent
                {
                    Input = resumedEvent.Reason,
                };
                break;
            default:
                throw new NotSupportedException($"Found unsupported history event '{e.EventType}'.");
        }

        return payload;
    }

    /// <summary>
    /// Converts an orchestrator action from protobuf format.
    /// </summary>
    /// <param name="a">The protobuf orchestrator action.</param>
    /// <returns>The converted orchestrator action.</returns>
    /// <exception cref="NotSupportedException">Thrown if the action type is not supported.</exception>
    public static OrchestratorAction ToOrchestratorAction(Proto.OrchestratorAction a)
    {
        switch (a.OrchestratorActionTypeCase)
        {
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.ScheduleTask:
                return new GrpcScheduleTaskOrchestratorAction
                {
                    Id = a.Id,
                    Input = a.ScheduleTask.Input,
                    Name = a.ScheduleTask.Name,
                    Version = a.ScheduleTask.Version,
                    ParentTraceContext = a.ScheduleTask.ParentTraceContext is not null
                        ? new DistributedTraceContext(a.ScheduleTask.ParentTraceContext.TraceParent, a.ScheduleTask.ParentTraceContext.TraceState)
                        : null,
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.CreateSubOrchestration:
                return new GrpcCreateSubOrchestrationAction
                {
                    Id = a.Id,
                    Input = a.CreateSubOrchestration.Input,
                    Name = a.CreateSubOrchestration.Name,
                    InstanceId = a.CreateSubOrchestration.InstanceId,
                    ParentTraceContext = a.CreateSubOrchestration.ParentTraceContext is not null
                        ? new DistributedTraceContext(a.CreateSubOrchestration.ParentTraceContext.TraceParent, a.CreateSubOrchestration.ParentTraceContext.TraceState)
                        : null,
                    Tags = null, // TODO
                    Version = a.CreateSubOrchestration.Version,
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.CreateTimer:
                return new CreateTimerOrchestratorAction
                {
                    Id = a.Id,
                    FireAt = a.CreateTimer.FireAt.ToDateTime(),
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.SendEvent:
                return new SendEventOrchestratorAction
                {
                    Id = a.Id,
                    Instance = new OrchestrationInstance
                    {
                        InstanceId = a.SendEvent.Instance.InstanceId,
                        ExecutionId = a.SendEvent.Instance.ExecutionId,
                    },
                    EventName = a.SendEvent.Name,
                    EventData = a.SendEvent.Data,
                };
            case Proto.OrchestratorAction.OrchestratorActionTypeOneofCase.CompleteOrchestration:
                var completedAction = a.CompleteOrchestration;
                var action = new OrchestrationCompleteOrchestratorAction
                {
                    Id = a.Id,
                    OrchestrationStatus = (OrchestrationStatus)completedAction.OrchestrationStatus,
                    Result = completedAction.Result,
                    Details = completedAction.Details,
                    FailureDetails = GetFailureDetails(completedAction.FailureDetails),
                    NewVersion = completedAction.NewVersion,
                };

                if (completedAction.CarryoverEvents?.Count > 0)
                {
                    foreach (var e in completedAction.CarryoverEvents)
                    {
                        // Only raised events are supported for carryover
                        if (e.EventRaised is Proto.EventRaisedEvent eventRaised)
                        {
                            action.CarryoverEvents.Add(new EventRaisedEvent(e.EventId, eventRaised.Input)
                            {
                                Name = eventRaised.Name,
                            });
                        }
                    }
                }

                return action;
            default:
                throw new NotSupportedException($"Received unsupported action type '{a.OrchestratorActionTypeCase}'.");
        }
    }

    /// <summary>
    /// Base64 encodes a protobuf message.
    /// </summary>
    /// <param name="message">The protobuf message to encode.</param>
    /// <returns>The base64 encoded string.</returns>
    public static string Base64Encode(IMessage message)
    {
        // Create a serialized payload using lower-level protobuf APIs. We do this to avoid allocating
        // byte[] arrays for every request, which would otherwise put a heavy burden on the GC. Unfortunately
        // the protobuf API version we're using doesn't currently have memory-efficient serialization APIs.
        int messageSize = message.CalculateSize();
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(messageSize);
        try
        {
            using MemoryStream intermediateBufferStream = new(rentedBuffer, 0, messageSize);
            CodedOutputStream protobufOutputStream = new(intermediateBufferStream);
            protobufOutputStream.WriteRawMessage(message);
            protobufOutputStream.Flush();
            return Convert.ToBase64String(rentedBuffer, 0, messageSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>
    /// Converts a MapField to a dictionary.
    /// </summary>
    /// <param name="properties">The MapField to convert.</param>
    /// <returns>The converted dictionary.</returns>
    public static IDictionary<string, object?> ConvertMapToDictionary(MapField<string, Value> properties)
    {
        return properties.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertValueToObject(kvp.Value));
    }

    /// <summary>
    /// Converts the specified task failure details from proto format to a FailureDetails instance.
    /// </summary>
    /// <param name="failureDetails">The task failure details from the proto.</param>
    /// <returns>
    /// A <see cref="FailureDetails"/> object if <paramref name="failureDetails"/> is not null; otherwise, null.
    /// </returns>
    internal static FailureDetails? GetFailureDetails(Proto.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new FailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            GetFailureDetails(failureDetails.InnerFailure),
            failureDetails.IsNonRetriable,
            ConvertMapToDictionary(failureDetails.Properties));
    }

    /// <summary>
    /// Convert FailureDetails class to proto format.
    /// </summary>
    /// <param name="failureDetails">The failure detials to convert.</param>
    /// <returns>Proto format of failure details.</returns>
    internal static Proto.TaskFailureDetails? GetFailureDetails(FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        var taskFailureDetails = new Proto.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType,
            ErrorMessage = failureDetails.ErrorMessage,
            StackTrace = failureDetails.StackTrace,
            InnerFailure = GetFailureDetails(failureDetails.InnerFailure),
            IsNonRetriable = failureDetails.IsNonRetriable,
        };

        // Add properties if they exist
        if (failureDetails.Properties != null)
        {
            foreach (var kvp in failureDetails.Properties)
            {
                taskFailureDetails.Properties.Add(kvp.Key, ConvertObjectToValue(kvp.Value));
            }
        }

        return taskFailureDetails;
    }

    /// <summary>
    /// Convert QueryInstancesRequest from protobuf format to OrchestrationQuery.
    /// </summary>
    /// <param name="request">Protobuf request to convert.</param>
    /// <returns>OrchestrationQuery instace.</returns>
    internal static OrchestrationQuery ToOrchestrationQuery(Proto.QueryInstancesRequest request)
    {
        var query = new OrchestrationQuery()
        {
            RuntimeStatus = request.Query.RuntimeStatus?.Select(status => (OrchestrationStatus)status).ToList(),
            CreatedTimeFrom = request.Query.CreatedTimeFrom?.ToDateTime(),
            CreatedTimeTo = request.Query.CreatedTimeTo?.ToDateTime(),
            TaskHubNames = request.Query.TaskHubNames,
            PageSize = request.Query.MaxInstanceCount,
            ContinuationToken = request.Query.ContinuationToken,
            InstanceIdPrefix = request.Query.InstanceIdPrefix,
            FetchInputsAndOutputs = request.Query.FetchInputsAndOutputs,
        };

        return query;
    }

    /// <summary>
    /// Creates a protobuf response for an instances query.
    /// </summary>
    /// <param name="result">The query result to serialize.</param>
    /// <param name="request">The original request that initiated the query.</param>
    /// <returns>The populated protobuf response.</returns>
    internal static Proto.QueryInstancesResponse CreateQueryInstancesResponse(OrchestrationQueryResult result, Proto.QueryInstancesRequest request)
    {
        Proto.QueryInstancesResponse response = new Proto.QueryInstancesResponse
        {
            ContinuationToken = result.ContinuationToken,
        };
        foreach (OrchestrationState state in result.OrchestrationState)
        {
            var orchestrationState = new Proto.OrchestrationState
            {
                InstanceId = state.OrchestrationInstance.InstanceId,
                Name = state.Name,
                Version = state.Version,
                Input = state.Input,
                Output = state.Output,
                ScheduledStartTimestamp = state.ScheduledStartTime == null ? null : Timestamp.FromDateTime(state.ScheduledStartTime.Value),
                CreatedTimestamp = Timestamp.FromDateTime(state.CreatedTime),
                LastUpdatedTimestamp = Timestamp.FromDateTime(state.LastUpdatedTime),
                OrchestrationStatus = (Proto.OrchestrationStatus)state.OrchestrationStatus,
                CustomStatus = state.Status,
            };
            response.OrchestrationState.Add(orchestrationState);
        }

        return response;
    }

    /// <summary>
    /// Convert PurgeInstancesRequest from protobuf format to PurgeInstanceFilter.
    /// </summary>
    /// <param name="request">Protobuf request to convert.</param>
    /// <returns>PurgeInstanceFilter instance.</returns>
    internal static PurgeInstanceFilter ToPurgeInstanceFilter(Proto.PurgeInstancesRequest request)
    {
        var purgeInstanceFilter = new PurgeInstanceFilter(
            request.PurgeInstanceFilter.CreatedTimeFrom.ToDateTime(),
            request.PurgeInstanceFilter.CreatedTimeTo?.ToDateTime(),
            request.PurgeInstanceFilter.RuntimeStatus?.Select(status => (OrchestrationStatus)status).ToList());
        return purgeInstanceFilter;
    }

    /// <summary>
    /// Creates a protobuf response for a purge operation.
    /// </summary>
    /// <param name="result">The purge result to serialize.</param>
    /// <returns>The populated protobuf response.</returns>
    internal static Proto.PurgeInstancesResponse CreatePurgeInstancesResponse(PurgeResult result)
    {
        Proto.PurgeInstancesResponse response = new Proto.PurgeInstancesResponse
        {
            DeletedInstanceCount = result.DeletedInstanceCount,
        };
        return response;
    }

    /// <summary>
    /// Converts a C# object to a protobuf Value.
    /// </summary>
    /// <param name="obj">The object to convert.</param>
    /// <returns>The converted protobuf Value.</returns>
    internal static Value ConvertObjectToValue(object? obj)
    {
        return obj switch
        {
            null => Value.ForNull(),
            string str => Value.ForString(str),
            bool b => Value.ForBool(b),
            int i => Value.ForNumber(i),
            long l => Value.ForNumber(l),
            float f => Value.ForNumber(f),
            double d => Value.ForNumber(d),
            decimal dec => Value.ForNumber((double)dec),

            // For DateTime and DateTimeOffset, add prefix to distinguish from normal string.
            DateTime dt => Value.ForString($"dt:{dt.ToString("O")}"),
            DateTimeOffset dto => Value.ForString($"dto:{dto.ToString("O")}"),
            IDictionary<string, object?> dict => Value.ForStruct(new Struct
            {
                Fields = { dict.ToDictionary(kvp => kvp.Key, kvp => ConvertObjectToValue(kvp.Value)) },
            }),
            IEnumerable e => Value.ForList(e.Cast<object?>().Select(ConvertObjectToValue).ToArray()),

            // Fallback: convert unlisted type to string.
            _ => Value.ForString(obj.ToString() ?? string.Empty),
        };
    }

    static object? ConvertValueToObject(Google.Protobuf.WellKnownTypes.Value value)
    {
        switch (value.KindCase)
        {
            case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NullValue:
                return null;
            case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.NumberValue:
                return value.NumberValue;
            case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StringValue:
                string stringValue = value.StringValue;

                // If the value starts with the 'dt:' prefix, it may represent a DateTime value — attempt to parse it.
                if (stringValue.StartsWith("dt:", StringComparison.Ordinal))
                {
                    if (DateTime.TryParse(stringValue[3..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date))
                    {
                        return date;
                    }
                }

                // If the value starts with the 'dto:' prefix, it may represent a DateTime value — attempt to parse it.
                if (stringValue.StartsWith("dto:", StringComparison.Ordinal))
                {
                    if (DateTimeOffset.TryParse(stringValue[4..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset date))
                    {
                        return date;
                    }
                }

                // Otherwise just return as string
                return stringValue;
            case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.BoolValue:
                return value.BoolValue;
            case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.StructValue:
                return value.StructValue.Fields.ToDictionary(
                    pair => pair.Key,
                    pair => ConvertValueToObject(pair.Value));
            case Google.Protobuf.WellKnownTypes.Value.KindOneofCase.ListValue:
                return value.ListValue.Values.Select(ConvertValueToObject).ToList();
            default:
                // Fallback: serialize the whole value to JSON string
                return JsonSerializer.Serialize(value);
        }
    }
}
