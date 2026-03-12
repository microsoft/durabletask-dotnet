// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// A plugin that tracks execution metrics for orchestrations and activities,
/// including counts (started, completed, failed) and durations.
/// </summary>
public sealed class MetricsPlugin : IDurableTaskPlugin
{
    /// <summary>
    /// The default plugin name.
    /// </summary>
    public const string DefaultName = "Microsoft.DurableTask.Metrics";

    readonly MetricsStore store;
    readonly IReadOnlyList<IOrchestrationInterceptor> orchestrationInterceptors;
    readonly IReadOnlyList<IActivityInterceptor> activityInterceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsPlugin"/> class.
    /// </summary>
    public MetricsPlugin()
        : this(new MetricsStore())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsPlugin"/> class with a shared store.
    /// </summary>
    /// <param name="store">The metrics store to use.</param>
    public MetricsPlugin(MetricsStore store)
    {
        Check.NotNull(store);
        this.store = store;
        this.orchestrationInterceptors = new List<IOrchestrationInterceptor>
        {
            new MetricsOrchestrationInterceptor(store),
        };
        this.activityInterceptors = new List<IActivityInterceptor>
        {
            new MetricsActivityInterceptor(store),
        };
    }

    /// <inheritdoc />
    public string Name => DefaultName;

    /// <inheritdoc />
    public IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors => this.orchestrationInterceptors;

    /// <inheritdoc />
    public IReadOnlyList<IActivityInterceptor> ActivityInterceptors => this.activityInterceptors;

    /// <summary>
    /// Gets the metrics store used by this plugin.
    /// </summary>
    public MetricsStore Store => this.store;

    sealed class MetricsOrchestrationInterceptor : IOrchestrationInterceptor
    {
        readonly MetricsStore store;

        public MetricsOrchestrationInterceptor(MetricsStore store) => this.store = store;

        public Task OnOrchestrationStartingAsync(OrchestrationInterceptorContext context)
        {
            this.store.IncrementOrchestrationStarted(context.Name);
            context.Properties["_metrics_stopwatch"] = Stopwatch.StartNew();
            return Task.CompletedTask;
        }

        public Task OnOrchestrationCompletedAsync(OrchestrationInterceptorContext context, object? result)
        {
            this.store.IncrementOrchestrationCompleted(context.Name);
            if (context.Properties.TryGetValue("_metrics_stopwatch", out object? sw) && sw is Stopwatch stopwatch)
            {
                stopwatch.Stop();
                this.store.RecordOrchestrationDuration(context.Name, stopwatch.Elapsed);
            }

            return Task.CompletedTask;
        }

        public Task OnOrchestrationFailedAsync(OrchestrationInterceptorContext context, Exception exception)
        {
            this.store.IncrementOrchestrationFailed(context.Name);
            if (context.Properties.TryGetValue("_metrics_stopwatch", out object? sw) && sw is Stopwatch stopwatch)
            {
                stopwatch.Stop();
                this.store.RecordOrchestrationDuration(context.Name, stopwatch.Elapsed);
            }

            return Task.CompletedTask;
        }
    }

    sealed class MetricsActivityInterceptor : IActivityInterceptor
    {
        readonly MetricsStore store;

        public MetricsActivityInterceptor(MetricsStore store) => this.store = store;

        public Task OnActivityStartingAsync(ActivityInterceptorContext context)
        {
            this.store.IncrementActivityStarted(context.Name);
            context.Properties["_metrics_stopwatch"] = Stopwatch.StartNew();
            return Task.CompletedTask;
        }

        public Task OnActivityCompletedAsync(ActivityInterceptorContext context, object? result)
        {
            this.store.IncrementActivityCompleted(context.Name);
            if (context.Properties.TryGetValue("_metrics_stopwatch", out object? sw) && sw is Stopwatch stopwatch)
            {
                stopwatch.Stop();
                this.store.RecordActivityDuration(context.Name, stopwatch.Elapsed);
            }

            return Task.CompletedTask;
        }

        public Task OnActivityFailedAsync(ActivityInterceptorContext context, Exception exception)
        {
            this.store.IncrementActivityFailed(context.Name);
            if (context.Properties.TryGetValue("_metrics_stopwatch", out object? sw) && sw is Stopwatch stopwatch)
            {
                stopwatch.Stop();
                this.store.RecordActivityDuration(context.Name, stopwatch.Elapsed);
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Thread-safe store for orchestration and activity execution metrics.
/// </summary>
public sealed class MetricsStore
{
    readonly ConcurrentDictionary<string, TaskMetrics> orchestrationMetrics = new();
    readonly ConcurrentDictionary<string, TaskMetrics> activityMetrics = new();

    /// <summary>
    /// Gets metrics for a specific orchestration by name.
    /// </summary>
    /// <param name="name">The orchestration name.</param>
    /// <returns>The metrics for the specified orchestration.</returns>
    public TaskMetrics GetOrchestrationMetrics(string name) =>
        this.orchestrationMetrics.GetOrAdd(name, _ => new TaskMetrics());

    /// <summary>
    /// Gets metrics for a specific activity by name.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <returns>The metrics for the specified activity.</returns>
    public TaskMetrics GetActivityMetrics(string name) =>
        this.activityMetrics.GetOrAdd(name, _ => new TaskMetrics());

    /// <summary>
    /// Gets all orchestration metrics.
    /// </summary>
    /// <returns>A read-only dictionary of orchestration name to metrics.</returns>
    public IReadOnlyDictionary<string, TaskMetrics> GetAllOrchestrationMetrics() => this.orchestrationMetrics;

    /// <summary>
    /// Gets all activity metrics.
    /// </summary>
    /// <returns>A read-only dictionary of activity name to metrics.</returns>
    public IReadOnlyDictionary<string, TaskMetrics> GetAllActivityMetrics() => this.activityMetrics;

    internal void IncrementOrchestrationStarted(TaskName name) => this.GetOrchestrationMetrics(name).IncrementStarted();

    internal void IncrementOrchestrationCompleted(TaskName name) => this.GetOrchestrationMetrics(name).IncrementCompleted();

    internal void IncrementOrchestrationFailed(TaskName name) => this.GetOrchestrationMetrics(name).IncrementFailed();

    internal void RecordOrchestrationDuration(TaskName name, TimeSpan duration) => this.GetOrchestrationMetrics(name).RecordDuration(duration);

    internal void IncrementActivityStarted(TaskName name) => this.GetActivityMetrics(name).IncrementStarted();

    internal void IncrementActivityCompleted(TaskName name) => this.GetActivityMetrics(name).IncrementCompleted();

    internal void IncrementActivityFailed(TaskName name) => this.GetActivityMetrics(name).IncrementFailed();

    internal void RecordActivityDuration(TaskName name, TimeSpan duration) => this.GetActivityMetrics(name).RecordDuration(duration);
}

/// <summary>
/// Thread-safe metrics for a single task (orchestration or activity).
/// </summary>
public sealed class TaskMetrics
{
    long started;
    long completed;
    long failed;
    long totalDurationTicks;

    /// <summary>
    /// Gets the number of times this task was started.
    /// </summary>
    public long Started => Interlocked.Read(ref this.started);

    /// <summary>
    /// Gets the number of times this task completed successfully.
    /// </summary>
    public long Completed => Interlocked.Read(ref this.completed);

    /// <summary>
    /// Gets the number of times this task failed.
    /// </summary>
    public long Failed => Interlocked.Read(ref this.failed);

    /// <summary>
    /// Gets the total accumulated duration across all executions.
    /// </summary>
    public TimeSpan TotalDuration => TimeSpan.FromTicks(Interlocked.Read(ref this.totalDurationTicks));

    internal void IncrementStarted() => Interlocked.Increment(ref this.started);

    internal void IncrementCompleted() => Interlocked.Increment(ref this.completed);

    internal void IncrementFailed() => Interlocked.Increment(ref this.failed);

    internal void RecordDuration(TimeSpan duration) => Interlocked.Add(ref this.totalDurationTicks, duration.Ticks);
}
