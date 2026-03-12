// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Interceptor for orchestration lifecycle events.
/// Implementations can run logic before and after orchestration execution.
/// </summary>
public interface IOrchestrationInterceptor
{
    /// <summary>
    /// Called before an orchestration begins execution (including replays).
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <returns>A task that completes when the interceptor logic is finished.</returns>
    Task OnOrchestrationStartingAsync(OrchestrationInterceptorContext context);

    /// <summary>
    /// Called after an orchestration completes execution successfully.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="result">The orchestration result, if any.</param>
    /// <returns>A task that completes when the interceptor logic is finished.</returns>
    Task OnOrchestrationCompletedAsync(OrchestrationInterceptorContext context, object? result);

    /// <summary>
    /// Called after an orchestration fails with an exception.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A task that completes when the interceptor logic is finished.</returns>
    Task OnOrchestrationFailedAsync(OrchestrationInterceptorContext context, Exception exception);
}
