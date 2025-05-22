// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Dapr.DurableTask.Worker;

namespace Dapr.DurableTask.ScheduledTasks;

/// <summary>
/// Extension methods for configuring Durable Task workers to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Adds scheduled tasks support to the worker builder.
    /// </summary>
    /// <param name="builder">The worker builder to add scheduled task support to.</param>
    public static void UseScheduledTasks(this IDurableTaskWorkerBuilder builder)
    {
        builder.AddTasks(r =>
        {
            r.AddEntity<Schedule>();
            r.AddOrchestrator<ExecuteScheduleOperationOrchestrator>();
        });
    }
}
