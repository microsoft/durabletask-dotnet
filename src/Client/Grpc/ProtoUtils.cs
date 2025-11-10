// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// Protobuf helpers and utilities.
/// </summary>
static class ProtoUtils
{
#pragma warning disable 0618 // Referencing Obsolete member. This is intention as we are only converting it.
    /// <summary>
    /// Converts <see cref="OrchestrationRuntimeStatus" /> to <see cref="P.OrchestrationStatus" />.
    /// </summary>
    /// <param name="status">The orchestration status.</param>
    /// <returns>A <see cref="P.OrchestrationStatus" />.</returns>
    internal static P.OrchestrationStatus ToGrpcStatus(this OrchestrationRuntimeStatus status)
        => status switch
        {
            OrchestrationRuntimeStatus.Canceled => P.OrchestrationStatus.Canceled,
            OrchestrationRuntimeStatus.Completed => P.OrchestrationStatus.Completed,
            OrchestrationRuntimeStatus.ContinuedAsNew => P.OrchestrationStatus.ContinuedAsNew,
            OrchestrationRuntimeStatus.Failed => P.OrchestrationStatus.Failed,
            OrchestrationRuntimeStatus.Pending => P.OrchestrationStatus.Pending,
            OrchestrationRuntimeStatus.Running => P.OrchestrationStatus.Running,
            OrchestrationRuntimeStatus.Terminated => P.OrchestrationStatus.Terminated,
            OrchestrationRuntimeStatus.Suspended => P.OrchestrationStatus.Suspended,
            _ => throw new ArgumentOutOfRangeException(nameof(status), "Unexpected value"),
        };
#pragma warning restore 0618 // Referencing Obsolete member.

    /// <summary>
    /// Converts <see cref="OrchestrationPriorityLevel" /> to <see cref="P.OrchestrationPriorityLevel" />.
    /// </summary>
    /// <param name="priorityLevel">The priority level.</param>
    /// <returns>A <see cref="P.OrchestrationPriorityLevel" />.</returns>
    internal static P.OrchestrationPriorityLevel ToGrpcPriorityLevel(this OrchestrationPriorityLevel priorityLevel)
        => priorityLevel switch
        {
            OrchestrationPriorityLevel.Unspecified => P.OrchestrationPriorityLevel.Unspecified,
            OrchestrationPriorityLevel.Urgent => P.OrchestrationPriorityLevel.Urgent,
            OrchestrationPriorityLevel.High => P.OrchestrationPriorityLevel.High,
            OrchestrationPriorityLevel.Normal => P.OrchestrationPriorityLevel.Normal,
            OrchestrationPriorityLevel.Low => P.OrchestrationPriorityLevel.Low,
            OrchestrationPriorityLevel.Background => P.OrchestrationPriorityLevel.Background,
            _ => throw new ArgumentOutOfRangeException(nameof(priorityLevel), "Unexpected value"),
        };
}
