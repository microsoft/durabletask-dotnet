// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.ScheduledTasks;

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

        // Register the feature for gRPC workers
        builder.Services
            .AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .PostConfigure(opt =>
            {
                opt.Capabilities.Add(P.WorkerCapability.ScheduledTasks);
            });
    }
}
