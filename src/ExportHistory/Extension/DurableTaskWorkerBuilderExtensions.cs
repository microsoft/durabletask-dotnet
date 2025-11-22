// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask.Worker;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Extension methods for configuring Durable Task workers to use the Azure Durable Task Scheduler service.
/// </summary>
public static class DurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Adds export history support to the worker builder.
    /// </summary>
    /// <param name="builder">The worker builder to add export history support to.</param>
    public static void UseExportHistory(this IDurableTaskWorkerBuilder builder)
    {
        builder.AddTasks(r =>
        {
            r.AddEntity<ExportJob>();
            r.AddOrchestrator<ExecuteExportJobOperationOrchestrator>();
            r.AddOrchestrator<ExportJobOrchestrator>();
            r.AddActivity<ExportInstanceHistoryActivity>();
            r.AddActivity<ListTerminalInstancesActivity>();
        });
    }
}
