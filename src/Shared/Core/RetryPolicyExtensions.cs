// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CoreRetryOptions = DurableTask.Core.RetryOptions;

namespace Microsoft.DurableTask;

/// <summary>
/// Extensions for <see cref="RetryPolicy" />.
/// </summary>
static class RetryPolicyExtensions
{
    /// <summary>
    /// Converts a <see cref="RetryPolicy" /> to a <see cref="CoreRetryOptions" />.
    /// </summary>
    /// <param name="retry">The retry policy.</param>
    /// <returns>A <see cref="CoreRetryOptions" />.</returns>
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
