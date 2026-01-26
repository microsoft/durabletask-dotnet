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
    /// Gets an array of all orchestration statuses.
    /// These are the statuses that can be used in OrchestrationIdReusePolicy.
    /// </summary>
    /// <returns>An immutable array of all orchestration statuses.</returns>
    public static ImmutableArray<P.OrchestrationStatus> GetAllStatuses()
    {
#pragma warning disable CS0618 // Type or member is obsolete - Canceled is intentionally included for compatibility
        // compatibility with what?
        return ImmutableArray.Create(
            P.OrchestrationStatus.Completed,
            P.OrchestrationStatus.Failed,
            P.OrchestrationStatus.Terminated,
            P.OrchestrationStatus.Canceled,
            P.OrchestrationStatus.Pending,
            P.OrchestrationStatus.Running,
            P.OrchestrationStatus.Suspended);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Converts dedupe statuses (statuses that should NOT be replaced) to an OrchestrationIdReusePolicy
    /// with replaceable statuses (statuses that CAN be replaced).
    /// </summary>
    /// <param name="dedupeStatuses">The orchestration statuses that should NOT be replaced. These are statuses for which an exception should be thrown if an orchestration already exists.</param>
    /// <returns>An OrchestrationIdReusePolicy with replaceable statuses set.</returns>
    /// <remarks>
    /// The policy uses "replaceableStatus" - these are statuses that CAN be replaced.
    /// dedupeStatuses are statuses that should NOT be replaced.
    /// So replaceableStatus = all statuses MINUS dedupeStatuses.
    /// </remarks>
    public static P.OrchestrationIdReusePolicy ConvertDedupeStatusesToReusePolicy(
        IEnumerable<P.OrchestrationStatus>? dedupeStatuses)
    {
        ImmutableArray<P.OrchestrationStatus> statuses = GetAllStatuses();
        ImmutableHashSet<P.OrchestrationStatus> dedupeStatusSet = dedupeStatuses?.ToImmutableHashSet() ?? ImmutableHashSet<P.OrchestrationStatus>.Empty;

        P.OrchestrationIdReusePolicy policy = new();

        // Add statuses that are NOT in dedupeStatuses as replaceable
        foreach (P.OrchestrationStatus status in statuses.Where(status => !dedupeStatusSet.Contains(status)))
        {
            policy.ReplaceableStatus.Add(status);
        }

        return policy;
    }

    /// <summary>
    /// Converts an OrchestrationIdReusePolicy with replaceable statuses to dedupe statuses
    /// (statuses that should NOT be replaced).
    /// </summary>
    /// <param name="policy">The OrchestrationIdReusePolicy containing replaceable statuses. If this parameter is null,
    /// then all statuses are considered replaceable.</param>
    /// <returns>An array of orchestration statuses that should NOT be replaced, or null if all statuses are replaceable.</returns>
    /// <remarks>
    /// The policy uses "replaceableStatus" - these are statuses that CAN be replaced.
    /// dedupeStatuses are statuses that should NOT be replaced (should throw exception).
    /// So dedupeStatuses = all statuses MINUS replaceableStatus.
    /// </remarks>
    public static P.OrchestrationStatus[]? ConvertReusePolicyToDedupeStatuses(
        P.OrchestrationIdReusePolicy? policy)
    {
        if (policy == null)
        {
            return null;
        }

        ImmutableArray<P.OrchestrationStatus> allStatuses = GetAllStatuses();
        ImmutableHashSet<P.OrchestrationStatus> replaceableStatusSet = policy.ReplaceableStatus.ToImmutableHashSet();

        // Calculate dedupe statuses = all statuses - replaceable statuses
        P.OrchestrationStatus[] dedupeStatuses = allStatuses
            .Where(status => !replaceableStatusSet.Contains(status))
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
