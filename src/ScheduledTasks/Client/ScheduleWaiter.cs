// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Provides waiter functionality for schedule state transitions.
/// </summary>
public class ScheduleWaiter : IScheduleWaiter
{
    readonly IScheduleHandle scheduleHandle;
    readonly TimeSpan defaultPollingInterval = TimeSpan.FromSeconds(5);
    readonly TimeSpan defaultTimeout = TimeSpan.FromMinutes(2);
    readonly TimeSpan defaultMaxPollingInterval = TimeSpan.FromSeconds(20);

    readonly string operationName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleWaiter"/> class.
    /// </summary>
    /// <param name="scheduleHandle">The schedule handle to wait on.</param>
    /// <param name="operationName"></param>
    public ScheduleWaiter(IScheduleHandle scheduleHandle, string operationName)
    {
        this.scheduleHandle = scheduleHandle ?? throw new ArgumentNullException(nameof(scheduleHandle));
        this.operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
    }

    /// <inheritdoc/>
    public Task<ScheduleDescription> WaitUntilPausedAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return this.WaitForStatusAsync(ScheduleStatus.Paused, options, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ScheduleDescription> WaitUntilActiveAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return this.WaitForStatusAsync(ScheduleStatus.Active, options, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> WaitUntilDeletedAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan timeout = options?.Timeout ?? this.defaultTimeout;
        TimeSpan pollingInterval = options?.PollingInterval ?? this.defaultPollingInterval;
        TimeSpan maxPollingInterval = options?.MaxPollingInterval ?? this.defaultMaxPollingInterval;
        bool useExponentialBackoff = options?.UseExponentialBackoff ?? false;
        double backoffMultiplier = options?.BackoffMultiplier ?? 2.0;

        using CancellationTokenSource timeoutCts = new CancellationTokenSource(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        TimeSpan currentPollingInterval = pollingInterval;

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                try
                {
                    await this.scheduleHandle.DescribeAsync();

                    // Calculate next polling interval with exponential backoff if enabled
                    if (useExponentialBackoff)
                    {
                        currentPollingInterval = TimeSpan.FromTicks(Math.Min(
                            currentPollingInterval.Ticks * (long)backoffMultiplier,
                            maxPollingInterval.Ticks));
                    }

                    await Task.Delay(currentPollingInterval, linkedCts.Token);
                }
                catch (ScheduleNotFoundException)
                {
                    return true;
                }
            }

            linkedCts.Token.ThrowIfCancellationRequested();
            throw new TimeoutException($"Timed out waiting for schedule {this.scheduleHandle.ScheduleId} to be deleted");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for schedule {this.scheduleHandle.ScheduleId} to be deleted");
        }
    }

    async Task<ScheduleDescription> WaitForStatusAsync(
        ScheduleStatus desiredStatus,
        WaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan timeout = options?.Timeout ?? this.defaultTimeout;
        TimeSpan pollingInterval = options?.PollingInterval ?? this.defaultPollingInterval;
        TimeSpan maxPollingInterval = options?.MaxPollingInterval ?? this.defaultMaxPollingInterval;
        bool useExponentialBackoff = options?.UseExponentialBackoff ?? false;
        double backoffMultiplier = options?.BackoffMultiplier ?? 2.0;

        using CancellationTokenSource timeoutCts = new CancellationTokenSource(timeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        TimeSpan currentPollingInterval = pollingInterval;

        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                try
                {
                    ScheduleDescription description = await this.scheduleHandle.DescribeAsync();
                    if (description.Status == desiredStatus)
                    {
                        return description;
                    }

                    // check latest operation log status
                    ScheduleActivityLog? latestActivityLog = description.ActivityLogs.LastOrDefault();
                    if (latestActivityLog != null && latestActivityLog.Status == ScheduleOperationStatus.Failed.ToString())
                    {
                        throw new ScheduleOperationFailedException(description.ScheduleId, latestActivityLog.Operation, latestActivityLog.Status, latestActivityLog.FailureDetails ?? null);
                    }
                }
                catch (Exception ex) when (ex is ScheduleNotFoundException)
                {
                    if (this.operationName == nameof(Schedule.CreateSchedule) && desiredStatus != ScheduleStatus.Active)
                    {
                        throw;
                    }
                }

                // Calculate next polling interval with exponential backoff if enabled
                if (useExponentialBackoff)
                {
                    currentPollingInterval = TimeSpan.FromTicks(Math.Min(
                        currentPollingInterval.Ticks * (long)backoffMultiplier,
                        maxPollingInterval.Ticks));
                }

                await Task.Delay(currentPollingInterval, linkedCts.Token);
            }

            linkedCts.Token.ThrowIfCancellationRequested();
            throw new TimeoutException($"Timed out waiting for schedule {this.scheduleHandle.ScheduleId} to reach status {desiredStatus}");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for schedule {this.scheduleHandle.ScheduleId} to reach status {desiredStatus}");
        }
    }
}
