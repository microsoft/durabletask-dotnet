// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Internal;

/// <summary>
/// Extension for <see cref="DurableTaskWorkerWorkItemFilters"/> to convert to gRPC types.
/// </summary>
public static class DurableTaskWorkerWorkItemFiltersExtensions
{
    /// <summary>
    /// Converts a <see cref="DurableTaskWorkerWorkItemFilters"/> to a gRPC <see cref="P.WorkItemFilters"/>.
    /// </summary>
    /// <param name="workItemFilter">The <see cref="DurableTaskWorkerWorkItemFilters"/> to convert.</param>
    /// <returns>A gRPC <see cref="P.WorkItemFilters"/>.</returns>
    public static P.WorkItemFilters ToGrpcWorkItemFilters(this DurableTaskWorkerWorkItemFilters workItemFilter)
    {
        var grpcWorkItemFilters = new P.WorkItemFilters();
        foreach (var orchestrationFilter in workItemFilter.Orchestrations)
        {
            var grpcOrchestrationFilter = new P.OrchestrationFilter
            {
                Name = orchestrationFilter.Name,
            };
            grpcOrchestrationFilter.Versions.AddRange(orchestrationFilter.Versions);
            grpcWorkItemFilters.Orchestrations.Add(grpcOrchestrationFilter);
        }

        foreach (var activityFilter in workItemFilter.Activities)
        {
            var grpcActivityAction = new P.ActivityFilter
            {
                Name = activityFilter.Name,
            };
            grpcActivityAction.Versions.AddRange(activityFilter.Versions);
            grpcWorkItemFilters.Activities.Add(grpcActivityAction);
        }

        foreach (var entityFilter in workItemFilter.Entities)
        {
            var grpcEntityAction = new P.EntityFilter
            {
                Name = entityFilter.Name,
            };
            grpcWorkItemFilters.Entities.Add(grpcEntityAction);
        }

        return grpcWorkItemFilters;
    }
}
