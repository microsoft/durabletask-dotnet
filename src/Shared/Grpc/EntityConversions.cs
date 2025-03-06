// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using DurableTask.Core;
using DurableTask.Core.Entities;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask;
using Newtonsoft.Json;
using DTCore = DurableTask.Core;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// Utilities for converting between representations of entity history events. The older backends represent entity
/// messages as external events, using a JSON encoding defined in DT Core.
/// Starting with the DTS backend, we use explicit, separate
/// protobuf encodings for all entity-related history events.
/// </summary>
static class EntityConversions
{
    // we copied the relevant data members from DT.Core to allow us to convert between the data structures
    // used in the backend, and the legacy encoding of entities within orchestration histories.

    // The point of this class is to reverse a Newtonsoft serialization that happened in prior DT code.
    // To do this reliably we use the same Newtonsoft.
    // This is not introducing a new dependency, and should be eliminated once the original dependency is eliminated.
    static readonly JsonSerializerSettings ConversionSettings = new JsonSerializerSettings()
    {
        TypeNameHandling = TypeNameHandling.None, // the type names do not match, so we have to disable them
    };

    /// <summary>
    /// Encodes an operation signal.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.HistoryEvent EncodeOperationSignaled(P.HistoryEvent protoEvent)
    {
        P.EntityOperationSignaledEvent signaledEvent = protoEvent.EntityOperationSignaled;
        DateTime? scheduledTime = signaledEvent.ScheduledTime?.ToDateTime();
        string name = EncodeEventName(scheduledTime);
        string input = JsonConvert.SerializeObject(
            new RequestMessage()
            {
                Operation = signaledEvent.Operation,
                IsSignal = true,
                Input = signaledEvent.Input,
                Id = signaledEvent.RequestId,
                ScheduledTime = scheduledTime,
            },
            ConversionSettings);

        string? target = signaledEvent.TargetInstanceId
            ?? throw new InvalidOperationException("missing target instance id");

        return CreateEventRaisedOrSentEvent(protoEvent.EventId, name, input, target);
    }

    /// <summary>
    /// Encodes an operation call.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <param name="instance">The orchestration instance.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.HistoryEvent EncodeOperationCalled(
        P.HistoryEvent protoEvent,
        OrchestrationInstance? instance)
    {
        P.EntityOperationCalledEvent calledEvent = protoEvent.EntityOperationCalled;
        DateTime? scheduledTime = calledEvent.ScheduledTime?.ToDateTime();
        string name = EncodeEventName(scheduledTime);
        string input = JsonConvert.SerializeObject(
            new RequestMessage()
            {
                Operation = calledEvent.Operation,
                IsSignal = false,
                Input = calledEvent.Input,
                Id = calledEvent.RequestId,
                ScheduledTime = scheduledTime,
                ParentInstanceId = instance?.InstanceId
                    ?? throw new InvalidOperationException("missing instance id"),
                ParentExecutionId = instance?.ExecutionId,
            },
            ConversionSettings);

        string? target = calledEvent.TargetInstanceId
            ?? throw new InvalidOperationException("missing target instance id");

        return CreateEventRaisedOrSentEvent(protoEvent.EventId, name, input, target);
    }

    /// <summary>
    /// Encodes an operation lock.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <param name="instance">The orchestration instance.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.HistoryEvent EncodeLockRequested(
        P.HistoryEvent protoEvent,
        OrchestrationInstance? instance)
    {
        P.EntityLockRequestedEvent lockRequestedEvent = protoEvent.EntityLockRequested;
        string name = EncodeEventName(null);
        string input = JsonConvert.SerializeObject(
            new RequestMessage()
            {
                Operation = null,
                Id = lockRequestedEvent.CriticalSectionId,
                LockSet = lockRequestedEvent.LockSet.Select(s => EntityId.FromString(s)).ToArray(),
                Position = lockRequestedEvent.Position,
                ParentInstanceId = instance?.InstanceId
                    ?? throw new InvalidOperationException("missing instance id"),
            },
            ConversionSettings);

        string? target = lockRequestedEvent.LockSet[lockRequestedEvent.Position];

        return CreateEventRaisedOrSentEvent(protoEvent.EventId, name, input, target);
    }

    /// <summary>
    /// Encodes an unlock message.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <param name="instance">The orchestration instance.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.HistoryEvent EncodeUnlockSent(
       P.HistoryEvent protoEvent,
       OrchestrationInstance? instance)
    {
        P.EntityUnlockSentEvent unlockSentEvent = protoEvent.EntityUnlockSent;
        string name = EncodeEventName(null);
        string input = JsonConvert.SerializeObject(
            new ReleaseMessage()
            {
                Id = unlockSentEvent.CriticalSectionId,
                ParentInstanceId = instance?.InstanceId
                    ?? throw new InvalidOperationException("missing instance id"),
            },
            ConversionSettings);

        string? target = unlockSentEvent.TargetInstanceId
            ?? throw new InvalidOperationException("missing target instance id");

        return CreateEventRaisedOrSentEvent(protoEvent.EventId, name, input, target);
    }

    /// <summary>
    /// Encodes a lock grant.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.EventRaisedEvent EncodeLockGranted(P.HistoryEvent protoEvent)
    {
        P.EntityLockGrantedEvent grantEvent = protoEvent.EntityLockGranted;
        return new DTCore.History.EventRaisedEvent(
            protoEvent.EventId,
            JsonConvert.SerializeObject(
                new ResponseMessage()
                {
                    Result = ResponseMessage.LockAcquisitionCompletion,
                },
                ConversionSettings))
        {
            Name = grantEvent.CriticalSectionId,
        };
    }

    /// <summary>
    /// Encodes an operation completion message.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.EventRaisedEvent EncodeOperationCompleted(P.HistoryEvent protoEvent)
    {
        P.EntityOperationCompletedEvent completedEvent = protoEvent.EntityOperationCompleted;
        return new DTCore.History.EventRaisedEvent(
            protoEvent.EventId,
            JsonConvert.SerializeObject(
                new ResponseMessage()
                {
                    Result = completedEvent.Output,
                },
                ConversionSettings))
        {
            Name = completedEvent.RequestId,
        };
    }

    /// <summary>
    /// Encodes an operation failed message.
    /// </summary>
    /// <param name="protoEvent">The proto event.</param>
    /// <returns>The core event.</returns>
    public static DTCore.History.EventRaisedEvent EncodeOperationFailed(P.HistoryEvent protoEvent)
    {
        P.EntityOperationFailedEvent failedEvent = protoEvent.EntityOperationFailed;
        return new DTCore.History.EventRaisedEvent(
            protoEvent.EventId,
            JsonConvert.SerializeObject(
                new ResponseMessage()
                {
                    ErrorMessage = failedEvent.FailureDetails.ErrorType,
                    Result = failedEvent.FailureDetails.ErrorMessage,
                    FailureDetails = failedEvent.FailureDetails.ToCore(),
                },
                ConversionSettings))
        {
            Name = failedEvent.RequestId,
        };
    }

    /// <summary>
    /// Decodes an orchestration action that sends an entity message from an external event.
    /// </summary>
    /// <param name="name">The name of the external event.</param>
    /// <param name="input">The input of the external event.</param>
    /// <param name="target">The target of the external event.</param>
    /// <param name="sendAction">The protobuf send action which should be assigned the correct action.</param>
    /// <param name="requestId">The request action.</param>
    internal static void DecodeEntityMessageAction(
       string name,
       string input,
       string? target,
       P.SendEntityMessageAction sendAction,
       out string requestId)
    {
        RequestMessage? message = JsonConvert.DeserializeObject<RequestMessage>(
            input,
            ConversionSettings);

        if (message == null)
        {
            throw new InvalidOperationException("Cannot convert null event");
        }

        if (message.Id == null)
        {
            throw new InvalidOperationException("missing ID");
        }

        if (name.StartsWith("op", System.StringComparison.Ordinal))
        {
            if (message.Operation == null)
            {
                // this is a lock request
                sendAction.EntityLockRequested = new P.EntityLockRequestedEvent
                {
                    CriticalSectionId = requestId = message.Id,
                    LockSet = { message.LockSet!.Select(e => e.ToString()) },
                    ParentInstanceId = message.ParentInstanceId,
                    Position = message.Position,
                };
            }
            else
            {
                // this is an operation call or signal
                Timestamp? scheduledTime = null;

                if (name.Length >= 3 && name[2] == '@' && DateTime.TryParse(name[3..], out DateTime time))
                {
                    scheduledTime = Timestamp.FromDateTime(time.ToUniversalTime());
                }

                if (message.IsSignal)
                {
                    sendAction.EntityOperationSignaled = new P.EntityOperationSignaledEvent
                    {
                        RequestId = requestId = message.Id,
                        Input = message.Input,
                        Operation = message.Operation,
                        ScheduledTime = scheduledTime,
                        TargetInstanceId = target,
                    };
                }
                else
                {
                    sendAction.EntityOperationCalled = new P.EntityOperationCalledEvent
                    {
                        RequestId = requestId = message.Id,
                        Input = message.Input,
                        Operation = message.Operation,
                        ScheduledTime = scheduledTime,
                        ParentInstanceId = message.ParentInstanceId,
                        TargetInstanceId = target,
                    };
                }
            }
        }
        else if (name == "release")
        {
            sendAction.EntityUnlockSent = new P.EntityUnlockSentEvent
            {
                CriticalSectionId = requestId = message.Id,
                TargetInstanceId = target,
                ParentInstanceId = message.ParentInstanceId,
            };
        }
        else
        {
            throw new InvalidOperationException($"Cannot convert event with name {name}");
        }
    }

    static string EncodeEventName(DateTime? scheduledTime)
        => scheduledTime.HasValue ? $"op@{scheduledTime.Value:o}" : "op";

    static DTCore.History.HistoryEvent CreateEventRaisedOrSentEvent(
        int eventId,
        string name,
        string input,
        string? target)
    {
        if (target == null)
        {
            // the event is used inside a message, so it does not include a target
            return new DTCore.History.EventRaisedEvent(eventId, input)
            {
                Name = name,
            };
        }
        else
        {
            // the event is used inside a history, so it includes a target instance
            return new DTCore.History.EventSentEvent(eventId)
            {
                Name = name,
                Input = input,
                InstanceId = target,
            };
        }
    }

    /// <summary>
    /// Copied from DT Core for serialization/deserialization.
    /// Modified so that only the relevant data is serialized/deserialized.
    /// </summary>
    [DataContract]
    class RequestMessage
    {
        /// <summary>
        /// Gets or sets the name of the operation being called (if this is an operation message) or <c>null</c>
        /// (if this is a lock request).
        /// </summary>
        [DataMember(Name = "op")]
        public string? Operation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether or not this is a one-way message.
        /// </summary>
        [DataMember(Name = "signal", EmitDefaultValue = false)]
        public bool IsSignal { get; set; }

        /// <summary>
        /// Gets or sets the operation input.
        /// </summary>
        [DataMember(Name = "input", EmitDefaultValue = false)]
        public string? Input { get; set; }

        /// <summary>
        /// Gets or sets a unique identifier for this operation. The original data type is GUID but since
        /// we use just strings in the backend, we may as well parse this as a string.
        /// </summary>
        [DataMember(Name = "id", IsRequired = true)]
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the parent instance that called this operation.
        /// </summary>
        [DataMember(Name = "parent", EmitDefaultValue = false)]
        public string? ParentInstanceId { get; set; }

        /// <summary>
        /// Gets or sets the parent instance that called this operation.
        /// </summary>
        [DataMember(Name = "parentExecution", EmitDefaultValue = false)]
        public string? ParentExecutionId { get; set; }

        /// <summary>
        /// Gets or sets an optional avlue, a scheduled time at which to start the operation.
        /// </summary>
        [DataMember(Name = "due", EmitDefaultValue = false)]
        public DateTime? ScheduledTime { get; set; }

        /// <summary>
        /// Gets or sets the lock set, the set of locks being acquired. Is sorted,
        /// contains at least one element, and has no repetitions.
        /// </summary>
        [DataMember(Name = "lockset", EmitDefaultValue = false)]
        public DTCore.Entities.EntityId[]? LockSet { get; set; }

        /// <summary>
        /// Gets or sets the message number For lock requests involving multiple locks.
        /// </summary>
        [DataMember(Name = "pos", EmitDefaultValue = false)]
        public int Position { get; set; }
    }

    /// <summary>
    /// Copied from DT Core for serialization/deserialization.
    /// Modified so that only the relevant data is serialized/deserialized.
    /// </summary>
    [DataContract]
    class ResponseMessage
    {
        public const string LockAcquisitionCompletion = "Lock Acquisition Completed";

        [DataMember(Name = "result")]
        public string? Result { get; set; }

        [DataMember(Name = "exceptionType", EmitDefaultValue = false)]
        public string? ErrorMessage { get; set; }

        [DataMember(Name = "failureDetails", EmitDefaultValue = false)]
        public FailureDetails? FailureDetails { get; set; }

        [IgnoreDataMember]
        public bool IsErrorResult => this.ErrorMessage != null;
    }

    /// <summary>
    /// Copied from DT Core for serialization/deserialization.
    /// Modified so that only the relevant data is serialized/deserialized.
    /// </summary>
    [DataContract]
    class ReleaseMessage
    {
        [DataMember(Name = "parent")]
        public string? ParentInstanceId { get; set; }

        [DataMember(Name = "id")]
        public string? Id { get; set; }
    }
}
