// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc;

static class ProtoUtils
{
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

    internal static Timestamp? ToTimestamp(this DateTime? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

    internal static Timestamp ToTimestamp(this DateTimeOffset dateTime) => Timestamp.FromDateTimeOffset(dateTime);

    internal static Timestamp? ToTimestamp(this DateTimeOffset? dateTime)
        => dateTime.HasValue ? dateTime.Value.ToTimestamp() : null;

#pragma warning disable 0618 // Referencing Obsolete member. This is intention as we are only converting it.
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
            _ => throw new ArgumentOutOfRangeException("Unexpected value", nameof(status)),
        };
#pragma warning restore 0618 // Referencing Obsolete member.

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
}
