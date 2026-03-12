// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// Interceptor for activity lifecycle events.
/// Implementations can run logic before and after activity execution.
/// </summary>
public interface IActivityInterceptor
{
    /// <summary>
    /// Called before an activity begins execution.
    /// </summary>
    /// <param name="context">The activity interceptor context.</param>
    /// <returns>A task that completes when the interceptor logic is finished.</returns>
    Task OnActivityStartingAsync(ActivityInterceptorContext context);

    /// <summary>
    /// Called after an activity completes execution successfully.
    /// </summary>
    /// <param name="context">The activity interceptor context.</param>
    /// <param name="result">The activity result, if any.</param>
    /// <returns>A task that completes when the interceptor logic is finished.</returns>
    Task OnActivityCompletedAsync(ActivityInterceptorContext context, object? result);

    /// <summary>
    /// Called after an activity fails with an exception.
    /// </summary>
    /// <param name="context">The activity interceptor context.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A task that completes when the interceptor logic is finished.</returns>
    Task OnActivityFailedAsync(ActivityInterceptorContext context, Exception exception);
}
