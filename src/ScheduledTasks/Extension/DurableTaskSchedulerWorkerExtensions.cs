// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.ScheduledTasks;

/// <summary>
/// Extension methods for configuring Durable Task workers to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskSchedulerWorkerExtensions
{
    /// <summary>
    /// Adds scheduled tasks support to the worker builder.
    /// </summary>
    /// <param name="builder">The worker builder to add scheduled task support to.</param>
    public static void UseScheduledTasks(this IDurableTaskWorkerBuilder builder)
    {
        builder.AddTasks(r =>
        {
            r.AddEntity(nameof(Schedule), sp => ActivatorUtilities.CreateInstance<Schedule>(sp));
            r.AddOrchestrator<ExecuteScheduleOperationOrchestrator>();
        });
    }
}
