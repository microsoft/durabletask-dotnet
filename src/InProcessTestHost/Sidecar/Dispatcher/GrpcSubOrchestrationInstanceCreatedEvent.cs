// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using DurableTask.Core.History;
using DurableTask.Core.Tracing;

namespace Microsoft.DurableTask.Testing.Sidecar.Dispatcher;

/// <summary>
/// gRPC-specific implementation of SubOrchestrationInstanceCreatedEvent that includes distributed tracing context.
/// </summary>
public class GrpcSubOrchestrationInstanceCreatedEvent : SubOrchestrationInstanceCreatedEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcSubOrchestrationInstanceCreatedEvent"/> class.
    /// </summary>
    /// <param name="eventId">The event ID.</param>
    public GrpcSubOrchestrationInstanceCreatedEvent(int eventId)
        : base(eventId)
    {
    }

    /// <summary>
    /// Gets or sets the parent trace context for distributed tracing.
    /// </summary>
    [DataMember]
    public DistributedTraceContext? ParentTraceContext { get; set; }
}
