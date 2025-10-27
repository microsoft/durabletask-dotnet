// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Testing.Sidecar.Dispatcher;

/// <summary>
/// Base class for dispatching and managing work items.
/// </summary>
/// <typeparam name="T">The type of work item to dispatch.</typeparam>
abstract class WorkItemDispatcher<T> : IDisposable where T : class
{
    static int nextDispatcherId; // CA1805: Remove explicit initialization

    readonly string name;
    readonly ILogger log;
    readonly ITrafficSignal trafficSignal;

    CancellationTokenSource? shutdownTcs;
    Task? workItemExecuteLoop;
    int currentWorkItems;
    bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemDispatcher{T}"/> class.
    /// </summary>
    /// <param name="log">The logger.</param>
    /// <param name="trafficSignal">The traffic signal.</param>
    public WorkItemDispatcher(ILogger log, ITrafficSignal trafficSignal)
    {
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.trafficSignal = trafficSignal;

        this.name = $"{this.GetType().Name}-{Interlocked.Increment(ref nextDispatcherId)}";
    }

    /// <summary>
    /// Gets the maximum number of concurrent work items.
    /// </summary>
    public virtual int MaxWorkItems => 10;

    /// <summary>
    /// Fetches the next work item asynchronously.
    /// </summary>
    /// <param name="timeout">The timeout for the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next work item, or null if none available.</returns>
    public abstract Task<T?> FetchWorkItemAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the specified work item.
    /// </summary>
    /// <param name="workItem">The work item to execute.</param>
    /// <returns>A task representing the execution.</returns>
    protected abstract Task ExecuteWorkItemAsync(T workItem);

    /// <summary>
    /// Releases the specified work item.
    /// </summary>
    /// <param name="workItem">The work item to release.</param>
    /// <returns>A task representing the release operation.</returns>
    public abstract Task ReleaseWorkItemAsync(T workItem);

    /// <summary>
    /// Abandons the specified work item.
    /// </summary>
    /// <param name="workItem">The work item to abandon.</param>
    /// <returns>A task representing the abandon operation.</returns>
    public abstract Task AbandonWorkItemAsync(T workItem);

    /// <summary>
    /// Renews the specified work item.
    /// </summary>
    /// <param name="workItem">The work item to renew.</param>
    /// <returns>The renewed work item.</returns>
    public abstract Task<T> RenewWorkItemAsync(T workItem);

    /// <summary>
    /// Gets the ID of the specified work item.
    /// </summary>
    /// <param name="workItem">The work item.</param>
    /// <returns>The work item ID.</returns>
    public abstract string GetWorkItemId(T workItem);

    /// <summary>
    /// Gets the delay in seconds after a fetch exception.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <returns>The delay in seconds.</returns>
    public abstract int GetDelayInSecondsOnFetchException(Exception ex);

    /// <summary>
    /// Starts the dispatcher.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the start operation.</returns>
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        // Dispatchers can be stopped and started back up again
        this.shutdownTcs?.Dispose();
        this.shutdownTcs = new CancellationTokenSource();

        this.workItemExecuteLoop = Task.Run(
            () => this.FetchAndExecuteLoop(this.shutdownTcs.Token),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the dispatcher.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the stop operation.</returns>
    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        // Trigger the cancellation tokens being used for background processing.
        this.shutdownTcs?.Cancel();

        // Wait for the execution loop to complete to ensure we're not scheduling any new work
        Task? executeLoop = this.workItemExecuteLoop;
        if (executeLoop != null)
        {
            await executeLoop.WaitAsync(cancellationToken);
        }

        // Wait for all outstanding work-item processing to complete for a fully graceful shutdown
        await this.WaitForOutstandingWorkItems(cancellationToken);
    }

    /// <summary>
    /// Disposes the dispatcher resources.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the dispatcher resources.
    /// </summary>
    /// <param name="disposing">Whether disposing from Dispose method.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed && disposing)
        {
            this.shutdownTcs?.Dispose();
            this.disposed = true;
        }
    }

    async Task WaitForAllClear(CancellationToken cancellationToken)
    {
        TimeSpan logInterval = TimeSpan.FromMinutes(1);

        // IMPORTANT: This logic assumes only a single logical "thread" is executing the receive loop,
        //            and that there's no possible race condition when comparing work-item counts.
        DateTime nextLogTime = DateTime.MinValue;
        while (this.currentWorkItems >= this.MaxWorkItems)
        {
            // Periodically log that we're waiting for available concurrency.
            // No need to use UTC for this. Local time is a bit easier to debug.
            DateTime now = DateTime.Now;
            if (now >= nextLogTime)
            {
                this.log.FetchingThrottled(
                    dispatcher: this.name,
                    details: "The current active work-item count has reached the allowed maximum.",
                    this.currentWorkItems,
                    this.MaxWorkItems);
                nextLogTime = now.Add(logInterval);
            }

            // CONSIDER: Use a notification instead of polling.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        // The dispatcher can also be paused by external signals.
        while (!await this.trafficSignal.WaitAsync(logInterval, cancellationToken))
        {
            this.log.FetchingThrottled(
                dispatcher: this.name,
                details: this.trafficSignal.WaitReason,
                this.currentWorkItems,
                this.MaxWorkItems);
        }
    }

    async Task WaitForOutstandingWorkItems(CancellationToken cancellationToken)
    {
        DateTime nextLogTime = DateTime.MinValue;
        while (this.currentWorkItems > 0)
        {
            // Periodically log that we're waiting for outstanding work items to complete.
            // No need to use UTC for this. Local time is a bit easier to debug.
            DateTime now = DateTime.Now;
            if (now >= nextLogTime)
            {
                this.log.DispatcherStopping(this.name, this.currentWorkItems);
                nextLogTime = now.AddMinutes(1);
            }

            // CONSIDER: Use a notification instead of polling.
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    // This method does not throw
    // CA1068: CancellationToken should be last parameter
    async Task DelayOnException(
        Exception exception,
        string workItemId,
        Func<Exception, int> delayInSecondsPolicy,
        CancellationToken cancellationToken)
    {
        try
        {
            int delaySeconds = delayInSecondsPolicy(exception);
            if (delaySeconds > 0)
            {
                await Task.Delay(delaySeconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down, do nothing
        }
        catch (Exception ex)
        {
            this.log.DispatchWorkItemFailure(
                dispatcher: this.name,
                action: "delay-on-exception",
                workItemId,
                details: ex.ToString());
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    async Task FetchAndExecuteLoop(CancellationToken cancellationToken)
    {
        try
        {
            // The work-item receive loop feeds the execution loop
            while (true)
            {
                T? workItem = null;
                try
                {
                    await this.WaitForAllClear(cancellationToken);

                    this.log.FetchWorkItemStarting(this.name, this.currentWorkItems, this.MaxWorkItems);
                    Stopwatch sw = Stopwatch.StartNew();

                    workItem = await this.FetchWorkItemAsync(Timeout.InfiniteTimeSpan, cancellationToken);

                    if (workItem != null)
                    {
                        this.currentWorkItems++;
                        this.log.FetchWorkItemCompleted(
                            this.name,
                            this.GetWorkItemId(workItem),
                            sw.ElapsedMilliseconds,
                            this.currentWorkItems,
                            this.MaxWorkItems);

                        // Run the execution on a background thread, which must never be canceled.
                        _ = Task.Run(() => this.ExecuteWorkItem(workItem), CancellationToken.None);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // shutting down
                    break;
                }
                catch (Exception ex)
                {
                    string unknownWorkItemId = "(unknown)";
                    this.log.DispatchWorkItemFailure(
                        dispatcher: this.name,
                        action: "fetchWorkItem",
                        workItemId: unknownWorkItemId,
                        details: ex.ToString());
                    await this.DelayOnException(ex, unknownWorkItemId, this.GetDelayInSecondsOnFetchException, cancellationToken);
                    continue;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
    }

    async Task ExecuteWorkItem(T workItem)
    {
        try
        {
            // Execute the work item and wait for it to complete
            await this.ExecuteWorkItemAsync(workItem);
        }
        catch (Exception ex)
        {
            this.log.DispatchWorkItemFailure(
                dispatcher: this.name,
                action: "execute",
                workItemId: this.GetWorkItemId(workItem),
                details: ex.ToString());

            await this.AbandonWorkItemAsync(workItem);
        }
        finally
        {
            try
            {
                await this.ReleaseWorkItemAsync(workItem);
            }
            catch (Exception ex)
            {
                // Best effort
                this.log.DispatchWorkItemFailure(
                    dispatcher: this.name,
                    action: "release",
                    workItemId: this.GetWorkItemId(workItem),
                    details: ex.ToString());
            }

            this.currentWorkItems--;
        }
    }
}

