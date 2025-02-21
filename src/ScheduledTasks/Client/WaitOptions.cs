// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Options for configuring wait behavior when waiting for schedule state transitions.
/// </summary>
public record WaitOptions
{
    /// <summary>
    /// Gets the timeout duration for the wait operation.
    /// If not specified, defaults to 5 minutes.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets the initial polling interval between status checks.
    /// If not specified, defaults to 5 seconds.
    /// </summary>
    public TimeSpan? PollingInterval { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use exponential backoff for polling intervals.
    /// When enabled, the polling interval will increase exponentially between retries.
    /// </summary>
    public bool UseExponentialBackoff { get; init; }

    /// <summary>
    /// Gets the maximum polling interval when using exponential backoff.
    /// Only applicable when UseExponentialBackoff is true.
    /// If not specified, defaults to 30 seconds.
    /// </summary>
    public TimeSpan? MaxPollingInterval { get; init; }

    /// <summary>
    /// Gets the exponential backoff multiplier.
    /// Only applicable when UseExponentialBackoff is true.
    /// If not specified, defaults to 2.0.
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;
}
