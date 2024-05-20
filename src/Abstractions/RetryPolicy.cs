﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A declarative retry policy that can be configured for activity or sub-orchestration calls.
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RetryPolicy"/> class.
    /// </summary>
    /// <param name="maxNumberOfAttempts">The maximum number of task invocation attempts. Must be 1 or greater.</param>
    /// <param name="firstRetryInterval">The amount of time to delay between the first and second attempt.</param>
    /// <param name="backoffCoefficient">
    /// The exponential back-off coefficient used to determine the delay between subsequent retries. Must be 1.0 or greater.
    /// </param>
    /// <param name="maxRetryInterval">
    /// The maximum time to delay between attempts, regardless of<paramref name="backoffCoefficient"/>.
    /// </param>
    /// <param name="retryTimeout">The overall timeout for retries.</param>
    /// <param name="handle">Delegate to call on exception to determine if retries should proceed.</param>
    /// <remarks>
    /// The value <see cref="Timeout.InfiniteTimeSpan"/> can be used to specify an unlimited timeout for
    /// <paramref name="maxRetryInterval"/> or <paramref name="retryTimeout"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if any of the following are true:
    /// <list type="bullet">
    ///   <item>The value for <paramref name="maxNumberOfAttempts"/> is less than or equal to zero.</item>
    ///   <item>The value for <paramref name="firstRetryInterval"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</item>
    ///   <item>The value for <paramref name="backoffCoefficient"/> is less than 1.0.</item>
    ///   <item>The value for <paramref name="maxRetryInterval"/> is less than <paramref name="firstRetryInterval"/>.</item>
    ///   <item>The value for <paramref name="retryTimeout"/> is less than <paramref name="firstRetryInterval"/>.</item>
    /// </list>
    /// </exception>
    public RetryPolicy(
        int maxNumberOfAttempts,
        TimeSpan firstRetryInterval,
        double backoffCoefficient = 1.0,
        TimeSpan? maxRetryInterval = null,
        TimeSpan? retryTimeout = null,
        Func<TaskFailedException, bool>? handle = null)
    {
        if (maxNumberOfAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(maxNumberOfAttempts),
                actualValue: maxNumberOfAttempts,
                message: $"The value for {nameof(maxNumberOfAttempts)} must be greater than zero.");
        }

        if (firstRetryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(firstRetryInterval),
                actualValue: firstRetryInterval,
                message: $"The value for {nameof(firstRetryInterval)} must be greater than zero.");
        }

        if (backoffCoefficient < 1.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(backoffCoefficient),
                actualValue: backoffCoefficient,
                message: $"The value for {nameof(backoffCoefficient)} must be greater or equal to 1.0.");
        }

        if (maxRetryInterval < firstRetryInterval && maxRetryInterval != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(maxRetryInterval),
                actualValue: maxRetryInterval,
                message: $"The value for {nameof(maxRetryInterval)} must be greater or equal to the value for {nameof(firstRetryInterval)}.");
        }

        if (retryTimeout < firstRetryInterval && retryTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                paramName: nameof(retryTimeout),
                actualValue: retryTimeout,
                message: $"The value for {nameof(retryTimeout)} must be greater or equal to the value for {nameof(firstRetryInterval)}.");
        }

        this.MaxNumberOfAttempts = maxNumberOfAttempts;
        this.FirstRetryInterval = firstRetryInterval;
        this.BackoffCoefficient = backoffCoefficient;
        this.MaxRetryInterval = maxRetryInterval ?? TimeSpan.FromHours(1);
        this.RetryTimeout = retryTimeout ?? Timeout.InfiniteTimeSpan;
        this.SetHandler(handle);
    }

    /// <summary>
    /// Gets the max number of attempts for executing a given task.
    /// </summary>
    public int MaxNumberOfAttempts { get; }

    /// <summary>
    /// Gets the amount of time to delay between the first and second attempt.
    /// </summary>
    public TimeSpan FirstRetryInterval { get; }

    /// <summary>
    /// Gets the exponential back-off coefficient used to determine the delay between subsequent retries.
    /// </summary>
    /// <value>
    /// Defaults to 1.0 for no back-off.
    /// </value>
    public double BackoffCoefficient { get; }

    /// <summary>
    /// Gets the maximum time to delay between attempts.
    /// </summary>
    /// <value>
    /// Defaults to 1 hour.
    /// </value>
    public TimeSpan MaxRetryInterval { get; }

    /// <summary>
    /// Gets the overall timeout for retries. No further attempts will be made at executing a task after this retry
    /// timeout expires.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </value>
    public TimeSpan RetryTimeout { get; }

    /// <summary>
    /// Gets a delegate to call on exception to determine if retries should proceed.
    /// </summary>
    /// <value>
    /// Defaults delegate that always returns true (i.e., all exceptions are retried).
    /// </value>
    public Func<Exception, bool> Handle { get; private set; }

    /// <summary>
    /// Set <see cref="Handle"/> delegate property.
    /// </summary>
    /// <param name="handle">
    /// Deletegate that receives <see cref="TaskFailedException"/> and returns boolean that
    /// determines if the task should be retried.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// This represents a defect in this library in that it should always receive wither
    /// <see cref="global::DurableTask.Core.Exceptions.TaskFailedException"/> or
    /// <see cref="global::DurableTask.Core.Exceptions.SubOrchestrationFailedException"/>.
    /// </exception>
    void SetHandler(Func<TaskFailedException, bool>? handle)
    {
        this.Handle = handle is null
            ? ((ex) => true)
            : ((ex) =>
            {
                if (ex is global::DurableTask.Core.Exceptions.TaskFailedException globalTaskFailedException)
                {
                    var taskFailedException = new TaskFailedException(globalTaskFailedException.Name, globalTaskFailedException.ScheduleId, globalTaskFailedException);
                    return handle.Invoke(taskFailedException);
                }
                else if (ex is global::DurableTask.Core.Exceptions.SubOrchestrationFailedException globalSubOrchestrationFailedException)
                {
                    var taskFailedException = new TaskFailedException(globalSubOrchestrationFailedException.Name, globalSubOrchestrationFailedException.ScheduleId, globalSubOrchestrationFailedException);
                    return handle.Invoke(taskFailedException);
                }
                else
                {
                    throw new InvalidOperationException("TaskFailedException nor SubOrchestrationFailedException were not received.");
                }
            });
    }


#pragma warning disable SA1623 // Property summary documentation should match accessors
    /// <summary>
    /// This functionality is not implemented. Will be removed in the future. Use TaskOptions.FromRetryHandler instead.
    /// </summary>
    [Obsolete("This functionality is not implemented. Will be removed in the future. Use TaskOptions.FromRetryHandler instead.")]
    public Func<Exception, Task<bool>>? HandleAsync { get; set; }
#pragma warning restore SA1623 // Property summary documentation should match accessors
}
