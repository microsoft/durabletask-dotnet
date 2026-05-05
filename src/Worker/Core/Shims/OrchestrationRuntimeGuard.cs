// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Provides runtime checks for detecting illegal asynchronous orchestrator execution.
/// </summary>
static class OrchestrationRuntimeGuard
{
    /// <summary>
    /// The error message used when orchestrator code resumes outside the orchestrator thread.
    /// </summary>
    internal const string IllegalAwaitErrorMessage = "An invalid asynchronous invocation was detected. This can be"
        + " caused by awaiting non-durable tasks in an orchestrator function's implementation or by middleware that"
        + " invokes asynchronous code.";

    /// <summary>
    /// Throws if the current thread is not the Durable Task orchestrator thread.
    /// </summary>
    internal static void ThrowIfIllegalAccess()
    {
        if (!OrchestrationContext.IsOrchestratorThread)
        {
            throw new InvalidOperationException(IllegalAwaitErrorMessage);
        }
    }
}
