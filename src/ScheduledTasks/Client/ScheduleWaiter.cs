// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Provides waiter functionality for schedule state transitions.
/// </summary>
public class ScheduleWaiter
{
    private readonly IScheduleHandle scheduleHandle;
    private readonly TimeSpan defaultPollingInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan defaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleWaiter"/> class.
    /// </summary>
    /// <param name="scheduleHandle">The schedule handle to wait on.</param>
    public ScheduleWaiter(IScheduleHandle scheduleHandle)
    {
        this.scheduleHandle = scheduleHandle ?? throw new ArgumentNullException(nameof(scheduleHandle));
    }

    /// <summary>
    /// Waits until the schedule is paused.
    /// </summary>
    /// <param name="timeout">Optional timeout duration. Defaults to 5 minutes.</param>
    /// <param name="pollingInterval">Optional polling interval. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The schedule description once paused.</returns>
    public Task<ScheduleDescription> WaitUntilPausedAsync(
        TimeSpan? timeout = null,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        return WaitForStatusAsync(ScheduleStatus.Paused, timeout, pollingInterval, cancellationToken);
    }

    /// <summary>
    /// Waits until the schedule is running.
    /// </summary>
    /// <param name="timeout">Optional timeout duration. Defaults to 5 minutes.</param>
    /// <param name="pollingInterval">Optional polling interval. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The schedule description once running.</returns>
    public Task<ScheduleDescription> WaitUntilRunningAsync(
        TimeSpan? timeout = null,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        return WaitForStatusAsync(ScheduleStatus.Running, timeout, pollingInterval, cancellationToken);
    }

    /// <summary>
    /// Waits until the schedule is deleted.
    /// </summary>
    /// <param name="timeout">Optional timeout duration. Defaults to 5 minutes.</param>
    /// <param name="pollingInterval">Optional polling interval. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The schedule description once deleted.</returns>
    public async Task<bool> WaitUntilDeletedAsync(
        TimeSpan? timeout = null,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= defaultTimeout;
        pollingInterval ??= defaultPollingInterval;

        using var timeoutCts = new CancellationTokenSource(timeout.Value);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                try
                {
                    await this.scheduleHandle.Describe();
                    await Task.Delay(pollingInterval.Value, linkedCts.Token);
                }
                catch (ScheduleNotFoundException)
                {
                    return true;
                }
            }

            linkedCts.Token.ThrowIfCancellationRequested();
            throw new TimeoutException($"Timed out waiting for schedule {scheduleHandle.ScheduleId} to be deleted");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for schedule {scheduleHandle.ScheduleId} to be deleted");
        }
    }

    /// <summary>
    /// Waits until the schedule reaches the specified status.
    /// </summary>
    /// <param name="desiredStatus">The status to wait for.</param>
    /// <param name="timeout">Optional timeout duration. Defaults to 5 minutes.</param>
    /// <param name="pollingInterval">Optional polling interval. Defaults to 5 seconds.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The schedule description once the desired status is reached.</returns>
    public async Task<ScheduleDescription> WaitForStatusAsync(
        ScheduleStatus desiredStatus,
        TimeSpan? timeout = null,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= defaultTimeout;
        pollingInterval ??= defaultPollingInterval;

        using var timeoutCts = new CancellationTokenSource(timeout.Value);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                var description = await this.scheduleHandle.Describe();
                if (description.Status == desiredStatus)
                {
                    return description;
                }

                await Task.Delay(pollingInterval.Value, linkedCts.Token);
            }

            linkedCts.Token.ThrowIfCancellationRequested();
            throw new TimeoutException($"Timed out waiting for schedule {scheduleHandle.ScheduleId} to reach status {desiredStatus}");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for schedule {scheduleHandle.ScheduleId} to reach status {desiredStatus}");
        }
    }
} 