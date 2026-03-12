// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// A plugin that applies token-bucket rate limiting to activity executions.
/// When the rate limit is exceeded, a <see cref="RateLimitExceededException"/> is thrown.
/// Rate limiting is applied per activity name.
/// </summary>
public sealed class RateLimitingPlugin : IDurableTaskPlugin
{
    /// <summary>
    /// The default plugin name.
    /// </summary>
    public const string DefaultName = "Microsoft.DurableTask.RateLimiting";

    readonly IReadOnlyList<IOrchestrationInterceptor> orchestrationInterceptors;
    readonly IReadOnlyList<IActivityInterceptor> activityInterceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingPlugin"/> class.
    /// </summary>
    /// <param name="options">The rate limiting options.</param>
    public RateLimitingPlugin(RateLimitingOptions options)
    {
        Check.NotNull(options);
        this.orchestrationInterceptors = Array.Empty<IOrchestrationInterceptor>();
        this.activityInterceptors = new List<IActivityInterceptor>
        {
            new RateLimitingActivityInterceptor(options),
        };
    }

    /// <inheritdoc />
    public string Name => DefaultName;

    /// <inheritdoc />
    public IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors => this.orchestrationInterceptors;

    /// <inheritdoc />
    public IReadOnlyList<IActivityInterceptor> ActivityInterceptors => this.activityInterceptors;

    /// <inheritdoc />
    public void RegisterTasks(DurableTaskRegistry registry)
    {
        // Rate limiting plugin is cross-cutting only; it does not register any tasks.
    }

    sealed class RateLimitingActivityInterceptor : IActivityInterceptor
    {
        readonly RateLimitingOptions options;
        readonly ConcurrentDictionary<string, TokenBucket> buckets = new();

        public RateLimitingActivityInterceptor(RateLimitingOptions options) => this.options = options;

        public Task OnActivityStartingAsync(ActivityInterceptorContext context)
        {
            string key = context.Name;
            TokenBucket bucket = this.buckets.GetOrAdd(key, _ => new TokenBucket(
                this.options.MaxTokens,
                this.options.RefillRate,
                this.options.RefillInterval));

            if (!bucket.TryConsume())
            {
                throw new RateLimitExceededException(
                    $"Rate limit exceeded for activity '{context.Name}'. " +
                    $"Max {this.options.MaxTokens} executions per {this.options.RefillInterval}.");
            }

            return Task.CompletedTask;
        }

        public Task OnActivityCompletedAsync(ActivityInterceptorContext context, object? result) =>
            Task.CompletedTask;

        public Task OnActivityFailedAsync(ActivityInterceptorContext context, Exception exception) =>
            Task.CompletedTask;
    }
}

/// <summary>
/// Options for the rate limiting plugin.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tokens (burst capacity).
    /// </summary>
    public int MaxTokens { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of tokens to refill per interval.
    /// </summary>
    public int RefillRate { get; set; } = 10;

    /// <summary>
    /// Gets or sets the interval between token refills.
    /// </summary>
    public TimeSpan RefillInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Exception thrown when a rate limit is exceeded.
/// </summary>
public sealed class RateLimitExceededException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RateLimitExceededException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thread-safe token bucket implementation for rate limiting.
/// </summary>
internal sealed class TokenBucket
{
    readonly int maxTokens;
    readonly int refillRate;
    readonly TimeSpan refillInterval;
    readonly object syncLock = new();
    int tokens;
    DateTime lastRefillTime;

    public TokenBucket(int maxTokens, int refillRate, TimeSpan refillInterval)
    {
        this.maxTokens = maxTokens;
        this.refillRate = refillRate;
        this.refillInterval = refillInterval;
        this.tokens = maxTokens;
        this.lastRefillTime = DateTime.UtcNow;
    }

    public bool TryConsume()
    {
        lock (this.syncLock)
        {
            this.Refill();

            if (this.tokens > 0)
            {
                this.tokens--;
                return true;
            }

            return false;
        }
    }

    void Refill()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan elapsed = now - this.lastRefillTime;

        if (elapsed >= this.refillInterval)
        {
            int intervalsElapsed = (int)(elapsed.Ticks / this.refillInterval.Ticks);
            int tokensToAdd = intervalsElapsed * this.refillRate;
            this.tokens = Math.Min(this.maxTokens, this.tokens + tokensToAdd);
            this.lastRefillTime = now;
        }
    }
}
