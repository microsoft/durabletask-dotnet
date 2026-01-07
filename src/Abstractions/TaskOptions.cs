// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Microsoft.DurableTask;

/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution.
/// </summary>
public record TaskOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOptions"/> class.
    /// </summary>
    /// <param name="retry">The task retry options.</param>
    public TaskOptions(TaskRetryOptions? retry)
        : this(retry, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOptions"/> class.
    /// </summary>
    /// <param name="retry">The task retry options.</param>
    /// <param name="tags">The tags to associate with the task.</param>
    public TaskOptions(TaskRetryOptions? retry = null, IDictionary<string, string>? tags = null)
    {
        this.Retry = retry;
        this.Tags = tags;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOptions"/> class by copying from another instance.
    /// </summary>
    /// <param name="options">The task options to copy from.</param>
    public TaskOptions(TaskOptions options)
    {
        Check.NotNull(options);
        this.Retry = options.Retry;
        this.Tags = options.Tags;
        this.CancellationToken = options.CancellationToken;
    }

    /// <summary>
    /// Gets the task retry options.
    /// </summary>
    public TaskRetryOptions? Retry { get; init; }

    /// <summary>
    /// Gets the tags to associate with the task.
    /// </summary>
    public IDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Gets the cancellation token that can be used to cancel the task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cancellation token provides cooperative cancellation for activities, sub-orchestrators, and retry logic.
    /// Due to the durable orchestrator execution model, cancellation only occurs at specific points when the
    /// orchestrator code is executing.
    /// </para>
    /// <para>
    /// <strong>Cancellation behavior:</strong>
    /// </para>
    /// <para>
    /// 1. <strong>Pre-scheduling check:</strong> If the token is cancelled before calling
    /// <c>CallActivityAsync</c> or <c>CallSubOrchestratorAsync</c>, a <see cref="TaskCanceledException"/> is thrown
    /// immediately without scheduling the task.
    /// </para>
    /// <para>
    /// 2. <strong>Retry handlers:</strong> The cancellation token is passed to custom retry handlers via
    /// <see cref="RetryContext"/>, allowing them to check for cancellation and stop retrying between attempts.
    /// </para>
    /// <para>
    /// <strong>Important limitation:</strong> Once an activity or sub-orchestrator is scheduled, the orchestrator
    /// yields execution and waits for the task to complete. During this yield period, the orchestrator code is not
    /// running, so it cannot respond to cancellation requests. Cancelling the token while waiting will not wake up
    /// the orchestrator or cancel the waiting task. This is a fundamental limitation of the durable orchestrator
    /// execution model.
    /// </para>
    /// <para>
    /// Note: Cancelling a parent orchestrator's token does not terminate sub-orchestrator instances that have
    /// already been scheduled.
    /// </para>
    /// <example>
    /// Example of pre-scheduling cancellation:
    /// <code>
    /// using CancellationTokenSource cts = new CancellationTokenSource();
    /// cts.Cancel(); // Cancel before scheduling
    ///
    /// TaskOptions options = new TaskOptions { CancellationToken = cts.Token };
    ///
    /// try
    /// {
    ///     // This will throw TaskCanceledException without scheduling the activity
    ///     string result = await context.CallActivityAsync&lt;string&gt;("MyActivity", "input", options);
    /// }
    /// catch (TaskCanceledException)
    /// {
    ///     // Handle cancellation
    /// }
    /// </code>
    /// </example>
    /// <example>
    /// Example of using cancellation with retry logic:
    /// <code>
    /// using CancellationTokenSource cts = new CancellationTokenSource();
    /// TaskOptions options = new TaskOptions
    /// {
    ///     Retry = TaskRetryOptions.FromRetryHandler(retryContext =>
    ///     {
    ///         if (retryContext.CancellationToken.IsCancellationRequested)
    ///         {
    ///             return false; // Stop retrying
    ///         }
    ///         return retryContext.LastAttemptNumber &lt; 3;
    ///     }),
    ///     CancellationToken = cts.Token
    /// };
    ///
    /// await context.CallActivityAsync("MyActivity", "input", options);
    /// </code>
    /// </example>
    /// </remarks>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Returns a new <see cref="TaskOptions" /> from the provided <see cref="RetryPolicy" />.
    /// </summary>
    /// <param name="policy">The policy to convert from.</param>
    /// <returns>A <see cref="TaskOptions" /> built from the policy.</returns>
    public static TaskOptions FromRetryPolicy(RetryPolicy policy) => new(policy);

    /// <summary>
    /// Returns a new <see cref="TaskOptions" /> from the provided <see cref="AsyncRetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    /// <returns>A <see cref="TaskOptions" /> built from the handler.</returns>
    public static TaskOptions FromRetryHandler(AsyncRetryHandler handler) => new(handler);

    /// <summary>
    /// Returns a new <see cref="TaskOptions" /> from the provided <see cref="RetryHandler" />.
    /// </summary>
    /// <param name="handler">The handler to convert from.</param>
    /// <returns>A <see cref="TaskOptions" /> built from the handler.</returns>
    public static TaskOptions FromRetryHandler(RetryHandler handler) => new(handler);

    /// <summary>
    /// Returns a new <see cref="SubOrchestrationOptions" /> with the provided instance ID. This can be used when
    /// starting a new sub-orchestration to specify the instance ID.
    /// </summary>
    /// <param name="instanceId">The instance ID to use.</param>
    /// <returns>A new <see cref="SubOrchestrationOptions" />.</returns>
    public SubOrchestrationOptions WithInstanceId(string instanceId) => new(this, instanceId);
}

/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution. This derived type can be used to
/// supply extra options for orchestrations.
/// </summary>
public record SubOrchestrationOptions : TaskOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrchestrationOptions"/> class.
    /// </summary>
    /// <param name="retry">The task retry options.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    public SubOrchestrationOptions(TaskRetryOptions? retry = null, string? instanceId = null)
        : base(retry)
    {
        this.InstanceId = instanceId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrchestrationOptions"/> class.
    /// </summary>
    /// <param name="options">The task options to wrap.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    public SubOrchestrationOptions(TaskOptions options, string? instanceId = null)
        : base(options)
    {
        this.InstanceId = instanceId;
        if (options is SubOrchestrationOptions derived)
        {
            if (instanceId is null)
            {
                this.InstanceId = derived.InstanceId;
            }

            this.Version = derived.Version;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SubOrchestrationOptions"/> class by copying from another instance.
    /// </summary>
    /// <param name="options">The sub-orchestration options to copy from.</param>
    public SubOrchestrationOptions(SubOrchestrationOptions options)
        : base(options)
    {
        Check.NotNull(options);
        this.InstanceId = options.InstanceId;
        this.Version = options.Version;
    }

    /// <summary>
    /// Gets the orchestration instance ID.
    /// </summary>
    public string? InstanceId { get; init; }

    /// <summary>
    /// Gets the version to associate with the sub-orchestration instance.
    /// </summary>
    public TaskVersion? Version { get; init; }
}

/// <summary>
/// Options for submitting new orchestrations via the client.
/// </summary>
public record StartOrchestrationOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StartOrchestrationOptions"/> class.
    /// </summary>
    /// <param name="InstanceId">
    /// The unique ID of the orchestration instance to schedule. If not specified, a new GUID value is used.
    /// </param>
    /// <param name="StartAt">
    /// The time when the orchestration instance should start executing. If not specified or if a date-time in the past
    /// is specified, the orchestration instance will be scheduled immediately.
    /// </param>
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter - using PascalCase to maintain backward compatibility with positional record syntax
    public StartOrchestrationOptions(string? InstanceId = null, DateTimeOffset? StartAt = null)
#pragma warning restore SA1313
    {
        this.InstanceId = InstanceId;
        this.StartAt = StartAt;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StartOrchestrationOptions"/> class by copying from another instance.
    /// </summary>
    /// <param name="options">The start orchestration options to copy from.</param>
    public StartOrchestrationOptions(StartOrchestrationOptions options)
    {
        Check.NotNull(options);
        this.InstanceId = options.InstanceId;
        this.StartAt = options.StartAt;
        this.Tags = options.Tags;
        this.Version = options.Version;
        this.DedupeStatuses = options.DedupeStatuses;
    }

    /// <summary>
    /// Gets the unique ID of the orchestration instance to schedule. If not specified, a new GUID value is used.
    /// </summary>
    public string? InstanceId { get; init; }

    /// <summary>
    /// Gets the time when the orchestration instance should start executing. If not specified or if a date-time in the past
    /// is specified, the orchestration instance will be scheduled immediately.
    /// </summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>
    /// Gets the tags to associate with the orchestration instance.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = ImmutableDictionary.Create<string, string>();

    /// <summary>
    /// Gets the version to associate with the orchestration instance.
    /// </summary>
    public TaskVersion? Version { get; init; }

    /// <summary>
    /// Gets the orchestration runtime statuses that should be considered for deduplication.
    /// </summary>
    /// <remarks>
    /// For type-safe usage, use the WithDedupeStatuses extension method.
    /// </remarks>
    public IReadOnlyList<string>? DedupeStatuses { get; init; }
}
