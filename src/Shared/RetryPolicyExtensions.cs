// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

using CoreRetryOptions = global::DurableTask.Core.RetryOptions;

static class RetryPolicyExtensions
{
    public static CoreRetryOptions ToDurableTaskCoreRetryOptions(this RetryPolicy retry)
    {
        // The legacy framework doesn't support Timeout.InfiniteTimeSpan so we have to convert that
        // to TimeSpan.MaxValue when encountered.
        static TimeSpan ConvertInfiniteTimeSpans(TimeSpan timeout) =>
            timeout == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : timeout;

        return new CoreRetryOptions(retry.FirstRetryInterval, retry.MaxNumberOfAttempts)
        {
            BackoffCoefficient = retry.BackoffCoefficient,
            MaxRetryInterval = ConvertInfiniteTimeSpans(retry.MaxRetryInterval),
            RetryTimeout = ConvertInfiniteTimeSpans(retry.RetryTimeout),
        };
    }
}