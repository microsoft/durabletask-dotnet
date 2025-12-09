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
    /// with replaceable statuses (statuses that CAN be replaced).
    /// </summary>
    /// <param name="dedupeStatuses">The orchestration statuses that should NOT be replaced. These are statuses for which an exception should be thrown if an orchestration already exists.</param>
    /// <returns>An OrchestrationIdReusePolicy with replaceable statuses set, or null if all terminal statuses are dedupe statuses.</returns>
    /// <remarks>
    /// The policy uses "replaceableStatus" - these are statuses that CAN be replaced.
    /// dedupeStatuses are statuses that should NOT be replaced.
    /// So replaceableStatus = all terminal statuses MINUS dedupeStatuses.
    /// </remarks>
    public static P.OrchestrationIdReusePolicy? ConvertDedupeStatusesToReusePolicy(
        IEnumerable<P.OrchestrationStatus> dedupeStatuses)
    {
        ImmutableArray<P.OrchestrationStatus> terminalStatuses = GetTerminalStatuses();
        ImmutableHashSet<P.OrchestrationStatus> dedupeStatusSet = dedupeStatuses.ToImmutableHashSet();

        P.OrchestrationIdReusePolicy policy = new();

        // Add terminal statuses that are NOT in dedupeStatuses as replaceable
        foreach (P.OrchestrationStatus terminalStatus in terminalStatuses.Where(status => !dedupeStatusSet.Contains(status)))
        {
            policy.ReplaceableStatus.Add(terminalStatus);
        }

        // Only return policy if we have replaceable statuses
        return policy.ReplaceableStatus.Count > 0 ? policy : null;
    }

    /// <summary>
    /// Converts an OrchestrationIdReusePolicy with replaceable statuses to dedupe statuses
    /// (statuses that should NOT be replaced).
    /// </summary>
    /// <param name="policy">The OrchestrationIdReusePolicy containing replaceable statuses.</param>
    /// <returns>An array of orchestration statuses that should NOT be replaced, or null if all terminal statuses are replaceable.</returns>
    /// <remarks>
    /// The policy uses "replaceableStatus" - these are statuses that CAN be replaced.
    /// dedupeStatuses are statuses that should NOT be replaced (should throw exception).
    /// So dedupeStatuses = all terminal statuses MINUS replaceableStatus.
    /// </remarks>
    public static P.OrchestrationStatus[]? ConvertReusePolicyToDedupeStatuses(
        P.OrchestrationIdReusePolicy? policy)
    {
        if (policy == null || policy.ReplaceableStatus.Count == 0)
        {
            return null;
        }

        ImmutableArray<P.OrchestrationStatus> terminalStatuses = GetTerminalStatuses();
        ImmutableHashSet<P.OrchestrationStatus> replaceableStatusSet = policy.ReplaceableStatus.ToImmutableHashSet();

        // Calculate dedupe statuses = terminal statuses - replaceable statuses
        List<P.OrchestrationStatus> dedupeStatuses = new();
        foreach (P.OrchestrationStatus terminalStatus in terminalStatuses)
        {
            if (!replaceableStatusSet.Contains(terminalStatus))
            {
                dedupeStatuses.Add(terminalStatus);
            }
        }

        // Only return if there are dedupe statuses
        return dedupeStatuses.Count > 0 ? dedupeStatuses.ToArray() : null;
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
