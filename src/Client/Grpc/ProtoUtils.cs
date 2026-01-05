// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Linq;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// Protobuf helpers and utilities.
/// </summary>
public static class ProtoUtils
{
    /// <summary>
    /// Gets the terminal orchestration statuses that are commonly used for deduplication.
    /// These are the statuses that can be used in OrchestrationIdReusePolicy.
    /// </summary>
    /// <returns>An immutable array of terminal orchestration statuses.</returns>
    public static ImmutableArray<P.OrchestrationStatus> GetTerminalStatuses()
    {
#pragma warning disable CS0618 // Type or member is obsolete - Canceled is intentionally included for compatibility
        return ImmutableArray.Create(
            P.OrchestrationStatus.Completed,
            P.OrchestrationStatus.Failed,
            P.OrchestrationStatus.Terminated,
            P.OrchestrationStatus.Canceled);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Converts dedupe statuses (statuses that should NOT be replaced) to an OrchestrationIdReusePolicy
    /// with TERMINATE action for terminal statuses that CAN be replaced.
    /// </summary>
    /// <param name="dedupeStatuses">The orchestration statuses that should NOT be replaced. These are statuses for which an exception should be thrown if an orchestration already exists.</param>
    /// <returns>An OrchestrationIdReusePolicy with TERMINATE action and operation statuses set, or null if all terminal statuses are dedupe statuses.</returns>
    /// <remarks>
    /// This method maintains backward compatibility by converting dedupe statuses to the new policy format.
    /// The policy will have action = TERMINATE and operationStatus = terminal statuses that can be replaced.
    /// dedupeStatuses are statuses that should NOT be replaced (ERROR action).
    /// So operationStatus = all terminal statuses MINUS dedupeStatuses.
    /// </remarks>
    public static P.OrchestrationIdReusePolicy? ConvertDedupeStatusesToReusePolicy(
        IEnumerable<P.OrchestrationStatus>? dedupeStatuses)
    {
        ImmutableArray<P.OrchestrationStatus> terminalStatuses = GetTerminalStatuses();
        ImmutableHashSet<P.OrchestrationStatus> dedupeStatusSet = dedupeStatuses?.ToImmutableHashSet() ?? ImmutableHashSet<P.OrchestrationStatus>.Empty;

        P.OrchestrationIdReusePolicy policy = new()
        {
            Action = P.CreateOrchestrationAction.Terminate,
        };

        // Add terminal statuses that are NOT in dedupeStatuses to operation status (these can be terminated and replaced)
        foreach (P.OrchestrationStatus terminalStatus in terminalStatuses.Where(status => !dedupeStatusSet.Contains(status)))
        {
            policy.OperationStatus.Add(terminalStatus);
        }

        // Only return policy if we have operation statuses
        return policy.OperationStatus.Count > 0 ? policy : null;
    }

    /// <summary>
    /// Converts a public CreateOrchestrationAction to a protobuf CreateOrchestrationAction.
    /// </summary>
    /// <param name="action">The public action.</param>
    /// <returns>A protobuf CreateOrchestrationAction.</returns>
    internal static P.CreateOrchestrationAction ConvertToProtoAction(
        Microsoft.DurableTask.Client.CreateOrchestrationAction action)
        => action switch
        {
            Microsoft.DurableTask.Client.CreateOrchestrationAction.Error => P.CreateOrchestrationAction.Error,
            Microsoft.DurableTask.Client.CreateOrchestrationAction.Ignore => P.CreateOrchestrationAction.Ignore,
            Microsoft.DurableTask.Client.CreateOrchestrationAction.Terminate => P.CreateOrchestrationAction.Terminate,
            _ => throw new ArgumentOutOfRangeException(nameof(action), "Unexpected value"),
        };

    /// <summary>
    /// Converts a public OrchestrationIdReusePolicy to a protobuf OrchestrationIdReusePolicy.
    /// </summary>
    /// <param name="policy">The public orchestration ID reuse policy.</param>
    /// <returns>A protobuf OrchestrationIdReusePolicy.</returns>
    public static P.OrchestrationIdReusePolicy? ConvertToProtoReusePolicy(
        Microsoft.DurableTask.Client.OrchestrationIdReusePolicy? policy)
    {
        if (policy == null)
        {
            return null;
        }

        P.OrchestrationIdReusePolicy protoPolicy = new()
        {
            Action = ConvertToProtoAction(policy.Action),
        };

        foreach (OrchestrationRuntimeStatus status in policy.OperationStatuses)
        {
            protoPolicy.OperationStatus.Add(status.ToGrpcStatus());
        }

        return protoPolicy;
    }

    /// <summary>
    /// Converts an OrchestrationIdReusePolicy to dedupe statuses (statuses that should NOT be replaced).
    /// </summary>
    /// <param name="policy">The OrchestrationIdReusePolicy containing action and operation statuses.</param>
    /// <returns>An array of orchestration statuses that should NOT be replaced, or null if all terminal statuses can be replaced.</returns>
    /// <remarks>
    /// This method maintains backward compatibility by converting the new policy format to dedupe statuses.
    /// For TERMINATE action: dedupeStatuses = all terminal statuses MINUS operationStatus.
    /// For ERROR or IGNORE action: the behavior depends on the action semantics.
    /// </remarks>
    public static P.OrchestrationStatus[]? ConvertReusePolicyToDedupeStatuses(
        P.OrchestrationIdReusePolicy? policy)
    {
        if (policy == null || policy.OperationStatus.Count == 0)
        {
            return null;
        }

        ImmutableArray<P.OrchestrationStatus> terminalStatuses = GetTerminalStatuses();
        ImmutableHashSet<P.OrchestrationStatus> operationStatusSet = policy.OperationStatus.ToImmutableHashSet();

        // For TERMINATE action: dedupe statuses = terminal statuses - operation status
        // For other actions, the conversion may not be straightforward
        P.OrchestrationStatus[] dedupeStatuses = terminalStatuses
            .Where(terminalStatus => !operationStatusSet.Contains(terminalStatus))
            .ToArray();

        // Only return if there are dedupe statuses
        return dedupeStatuses.Length > 0 ? dedupeStatuses : null;
    }

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
}
