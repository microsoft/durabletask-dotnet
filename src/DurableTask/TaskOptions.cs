// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableTask;

/// <summary>
/// Options that can be used to control the behavior of orchestrator task execution.
/// </summary>
public class TaskOptions
{
    internal TaskOptions(Builder builder)
    {
        this.RetryPolicy = builder.RetryPolicy;
        this.RetryHandler = builder.RetryHandler;
    }

    /// <summary>
    /// Gets the retry policy that was configured for this <see cref="TaskOptions"/> instance.
    /// </summary>
    public RetryPolicy? RetryPolicy { get; }

    /// <summary>
    /// Gets the cancellation token that was configured for this <see cref="TaskOptions"/> instance.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    internal AsyncRetryHandler? RetryHandler { get; }

    /// <summary>
    /// Convenience method from creating a <see cref="TaskOptions"/> object from a <see cref="RetryPolicy"/>.
    /// </summary>
    /// <param name="policy">The task retry policy to configure.</param>
    /// <param name="cancellationToken">Optional cancellation token for canceling the task.</param>
    /// <returns>Returns a newly created <see cref="TaskOptions"/> object.</returns>
    public static TaskOptions FromRetryPolicy(RetryPolicy policy, CancellationToken cancellationToken = default)
    {
        return CreateBuilder().WithRetryStrategy(policy).WithCancellationToken(cancellationToken).Build();
    }

    /// <summary>
    /// Convenience method from creating a <see cref="TaskOptions"/> object from a <see cref="RetryHandler"/>.
    /// </summary>
    /// <param name="retryHandler">The task retry handler to configure.</param>
    /// <param name="cancellationToken">Optional cancellation token for canceling the task.</param>
    /// <returns>Returns a newly created <see cref="TaskOptions"/> object.</returns>
    public static TaskOptions FromRetryHandler(RetryHandler retryHandler, CancellationToken cancellationToken = default)
    {
        return CreateBuilder().WithRetryStrategy(retryHandler).WithCancellationToken(cancellationToken).Build();
    }

    /// <summary>
    /// Convenience method from creating a <see cref="TaskOptions"/> object from a <see cref="AsyncRetryHandler"/>.
    /// </summary>
    /// <inheritdoc cref="FromRetryHandler(RetryHandler, CancellationToken)"/>
    public static TaskOptions FromRetryHandler(AsyncRetryHandler retryHandler, CancellationToken cancellationToken = default)
    {
        return CreateBuilder().WithRetryStrategy(retryHandler).WithCancellationToken(cancellationToken).Build();
    }

    /// <summary>
    /// Creates a new <see cref="Builder"/> object that can be used to construct a new <see cref="TaskOptions"/> object.
    /// </summary>
    /// <returns>Returns a new <see cref="Builder"/> object that can be used to construct a new <see cref="TaskOptions"/> object.</returns>
    public static Builder CreateBuilder() => new();

    /// <summary>
    /// Builder for creating <see cref="TaskOptions"/> instances.
    /// </summary>
    public sealed class Builder
    {
        internal RetryPolicy? RetryPolicy { get; private set; }

        internal AsyncRetryHandler? RetryHandler { get; private set; }

        internal CancellationToken CancellationToken { get; private set; }

        /// <summary>
        /// Configures a task retry policy.
        /// </summary>
        /// <param name="policy">The task retry policy to configure.</param>
        /// <returns>Returns the current <see cref="Builder"/> object.</returns>
        public Builder WithRetryStrategy(RetryPolicy policy)
        {
            this.RetryPolicy = policy;
            this.RetryHandler = null;
            return this;
        }

        /// <inheritdoc cref="WithRetryStrategy(AsyncRetryHandler)"/>
        public Builder WithRetryStrategy(RetryHandler handler)
        {
            // Synchronous handlers are wrapped in an async handler so that we only have
            // to keep track of a single handler assignment.
            return this.WithRetryStrategy(retryContext => Task.FromResult(handler(retryContext)));
        }

        /// <summary>
        /// Configures a retry handler.
        /// </summary>
        /// <param name="handler">The handler to invoke when deciding whether to retry a failed orchestrator task.</param>
        /// <returns>Returns the current <see cref="Builder"/> object.</returns>
        public Builder WithRetryStrategy(AsyncRetryHandler handler)
        {
            this.RetryHandler = handler;
            this.RetryPolicy = null;
            return this;
        }

        /// <summary>
        /// Configures a <see cref="CancellationToken"/> that can be used to cancel the task execution.
        /// </summary>
        /// <remarks>
        /// Cancellation tokens can be used to stop the current orchestrator from awaiting a pending activity or
        /// sub-orchestration completion. However, this cancellation won't necessarily stop the activity or
        /// sub-orchestration from running in the background.
        /// </remarks>
        /// <param name="cancellationToken">The cancellation token to use for cancelling task execution.</param>
        /// <returns>Returns the current <see cref="Builder"/> object.</returns>
        public Builder WithCancellationToken(CancellationToken cancellationToken)
        {
            if (cancellationToken != default)
            {
                throw new NotSupportedException("Durable task cancellation is not yet supported. See https://github.com/microsoft/durabletask-dotnet/issues/7 for more information.");
            }

            this.CancellationToken = cancellationToken;
            return this;
        }

        /// <summary>
        /// Creates a new <see cref="TaskOptions"/> object from this builder.
        /// </summary>
        /// <returns>The created <see cref="TaskOptions"/> object.</returns>
        public TaskOptions Build() => new(this);
    }
}
