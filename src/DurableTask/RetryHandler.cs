// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DurableTask;

/// <summary>
/// Delegate for manually handling task retries.
/// </summary>
/// <param name="retryContext">Retry context that's updated between each retry attempt.</param>
/// <returns>Returns <c>true</c> to continue retrying or <c>false</c> to stop retrying.</returns>
public delegate bool RetryHandler(RetryContext retryContext);

/// <inheritdoc cref="RetryHandler"/>
public delegate Task<bool> AsyncRetryHandler(RetryContext retryContext);

/// <summary>
/// Retry context data that's provided to task retry handler implementations.
/// </summary>
/// <param name="OrchestrationContext">The context of the parent orchestrator.</param>
/// <param name="LastAttemptNumber">The current attempt number experiencing a failure.</param>
/// <param name="LastFailure">The details of the previous task failure.</param>
/// <param name="TotalRetryTime">The total amount of time spent in a retry loop for the current task.</param>
/// <param name="CancellationToken">A cancellation token that can be used to cancel the retries.</param>
public record RetryContext(
    TaskOrchestrationContext OrchestrationContext,
    int LastAttemptNumber,
    TaskFailureDetails LastFailure,
    TimeSpan TotalRetryTime,
    CancellationToken CancellationToken);
