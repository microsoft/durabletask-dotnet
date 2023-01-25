// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Extensions for <see cref="TaskOrchestrationContext" /> for strongly-typed requests.
/// </summary>
public static class TaskOrchestrationContextRequestExtensions
{
    /// <summary>
    /// Runs the sub-orchestration described by <paramref name="request" /> with <paramref name="request" /> as the
    /// input to the orchestration itself.
    /// </summary>
    /// <typeparam name="TResult">The result type of the orchestration.</typeparam>
    /// <param name="context">The context used to run the orchestration.</param>
    /// <param name="request">The orchestration request.</param>
    /// <param name="options">The task options.</param>
    /// <returns>The result of the orchestration.</returns>
    public static Task<TResult> RunAsync<TResult>(
        this TaskOrchestrationContext context, IOrchestrationRequest<TResult> request, TaskOptions? options = null)
    {
        Check.NotNull(context);
        Check.NotNull(request);
        TaskName name = request.GetTaskName();
        return context.CallSubOrchestratorAsync<TResult>(name, request, options);
    }

    /// <summary>
    /// Runs the sub-orchestration described by <paramref name="request" /> with <paramref name="request" /> as the
    /// input to the orchestration itself.
    /// </summary>
    /// <param name="context">The context used to run the orchestration.</param>
    /// <param name="request">The orchestration request.</param>
    /// <param name="options">The task options.</param>
    /// <returns>A task that completes when the orchestration completes.</returns>
    public static Task RunAsync(
        this TaskOrchestrationContext context, IOrchestrationRequest<Unit> request, TaskOptions? options = null)
    {
        Check.NotNull(context);
        Check.NotNull(request);
        TaskName name = request.GetTaskName();
        return context.CallSubOrchestratorAsync(name, request, options);
    }
}
