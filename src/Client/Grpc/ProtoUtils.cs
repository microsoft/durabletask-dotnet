// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// Protobuf helpers and utilities.
/// </summary>
static class ProtoUtils
{
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
    /// Converts a <see cref="DateTime" /> to a gRPC <see cref="Timestamp" />.
    /// </summary>
    /// <param name="dateTime">The date-time to convert.</param>
    /// <returns>The gRPC timestamp.</returns>
    internal static Timestamp? ToTimestamp(this DateTimeOffset? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

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
    /// Converts a <see cref="P.TaskFailureDetails" /> to a <see cref="TaskFailureDetails" />.
    /// </summary>
    /// <param name="failureDetails">The failure details to convert.</param>
    /// <returns>The converted failure details.</returns>
    internal static TaskFailureDetails? ConvertTaskFailureDetails(P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new TaskFailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            ConvertTaskFailureDetails(failureDetails.InnerFailure));
    }

    /// <summary>
    /// Converts <see cref="Exception" /> to a <see cref="P.TaskFailureDetails" />.
    /// </summary>
    /// <param name="e">The exception to convert.</param>
    /// <returns>The converted failure details.</returns>
    internal static P.TaskFailureDetails? ToTaskFailureDetails(Exception? e)
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
            InnerFailure = ToTaskFailureDetails(e.InnerException),
        };
    }

    static FailureDetails? ConvertFailureDetails(P.TaskFailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new FailureDetails(
            failureDetails.ErrorType,
            failureDetails.ErrorMessage,
            failureDetails.StackTrace,
            ConvertFailureDetails(failureDetails.InnerFailure),
            failureDetails.IsNonRetriable);
    }

    static P.TaskFailureDetails? ConvertFailureDetails(FailureDetails? failureDetails)
    {
        if (failureDetails == null)
        {
            return null;
        }

        return new P.TaskFailureDetails
        {
            ErrorType = failureDetails.ErrorType ?? "(unkown)",
            ErrorMessage = failureDetails.ErrorMessage ?? "(unkown)",
            StackTrace = failureDetails.StackTrace,
            IsNonRetriable = failureDetails.IsNonRetriable,
            InnerFailure = ConvertFailureDetails(failureDetails.InnerFailure),
        };
    }
}
