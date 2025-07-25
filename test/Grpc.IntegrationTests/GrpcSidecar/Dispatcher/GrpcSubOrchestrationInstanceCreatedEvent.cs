// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using DurableTask.Core.History;
using DurableTask.Core.Tracing;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

public class GrpcSubOrchestrationInstanceCreatedEvent : SubOrchestrationInstanceCreatedEvent
{
    public GrpcSubOrchestrationInstanceCreatedEvent(int eventId)
        : base(eventId)
    {
    }

    [DataMember]
    public DistributedTraceContext? ParentTraceContext { get; set; }
}
