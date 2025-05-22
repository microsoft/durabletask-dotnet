// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Dapr.DurableTask;
using DurableTask.Core.Entities;
using Dapr.DurableTask.Entities;
using Core = DurableTask.Core;

namespace Dapr.DurableTask.Client;

/// <summary>
/// Extensions for interacting with DurableTask.Core.
/// </summary>
static class ShimExtensions
{
    /// <summary>
    /// Convert <see cref="Core.OrchestrationStatus" /> to <see cref="OrchestrationRuntimeStatus" />.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The runtime status.</returns>
    public static OrchestrationRuntimeStatus ConvertFromCore(this Core.OrchestrationStatus status)
    {
#pragma warning disable CS0618 // Do not use obsolete members. Justification: conversion.
        return status switch
        {
            // Use explicit conversions incase anything is re-ordered.
            Core.OrchestrationStatus.Running => OrchestrationRuntimeStatus.Running,
            Core.OrchestrationStatus.Completed => OrchestrationRuntimeStatus.Completed,
            Core.OrchestrationStatus.ContinuedAsNew => OrchestrationRuntimeStatus.ContinuedAsNew,
            Core.OrchestrationStatus.Failed => OrchestrationRuntimeStatus.Failed,
            Core.OrchestrationStatus.Canceled => OrchestrationRuntimeStatus.Canceled,
            Core.OrchestrationStatus.Terminated => OrchestrationRuntimeStatus.Terminated,
            Core.OrchestrationStatus.Pending => OrchestrationRuntimeStatus.Pending,
            Core.OrchestrationStatus.Suspended => OrchestrationRuntimeStatus.Suspended,
            _ => (OrchestrationRuntimeStatus)(int)status,
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Convert <see cref="OrchestrationRuntimeStatus" /> to <see cref="Core.OrchestrationStatus" />.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The orchestration status.</returns>
    public static Core.OrchestrationStatus ConvertToCore(this OrchestrationRuntimeStatus status)
    {
#pragma warning disable CS0618 // Do not use obsolete members. Justification: conversion.
        return status switch
        {
            // Use explicit conversions incase anything is re-ordered.
            OrchestrationRuntimeStatus.Running => Core.OrchestrationStatus.Running,
            OrchestrationRuntimeStatus.Completed => Core.OrchestrationStatus.Completed,
            OrchestrationRuntimeStatus.ContinuedAsNew => Core.OrchestrationStatus.ContinuedAsNew,
            OrchestrationRuntimeStatus.Failed => Core.OrchestrationStatus.Failed,
            OrchestrationRuntimeStatus.Canceled => Core.OrchestrationStatus.Canceled,
            OrchestrationRuntimeStatus.Terminated => Core.OrchestrationStatus.Terminated,
            OrchestrationRuntimeStatus.Pending => Core.OrchestrationStatus.Pending,
            OrchestrationRuntimeStatus.Suspended => Core.OrchestrationStatus.Suspended,
            _ => (Core.OrchestrationStatus)(int)status,
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Convert <see cref="Core.FailureDetails" /> to <see cref="Dapr.DurableTask.TaskFailureDetails" />.
    /// </summary>
    /// <param name="details">The details to convert.</param>
    /// <returns>The task failure details.</returns>
    [return: NotNullIfNotNull(nameof(details))]
    public static TaskFailureDetails? ConvertFromCore(this Core.FailureDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        TaskFailureDetails? inner = details.InnerFailure?.ConvertFromCore();
        return new TaskFailureDetails(details.ErrorType, details.ErrorMessage, details.StackTrace, inner);
    }

    /// <summary>
    /// Convert <see cref="Core.PurgeResult" /> to <see cref="PurgeResult" />.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>The purge result.</returns>
    [return: NotNullIfNotNull(nameof(result))]
    public static PurgeResult? ConvertFromCore(this Core.PurgeResult? result)
    {
        if (result is null)
        {
            return null;
        }

        return new PurgeResult(result.DeletedInstanceCount);
    }

    /// <summary>
    /// Convert <see cref="Core.PurgeResult" /> to <see cref="PurgeResult" />.
    /// </summary>
    /// <param name="filter">The result to convert.</param>
    /// <returns>The purge result.</returns>
    [return: NotNullIfNotNull(nameof(filter))]
    public static Core.PurgeInstanceFilter? ConvertToCore(this PurgeInstancesFilter? filter)
    {
        if (filter is null)
        {
            return null;
        }

        IEnumerable<Core.OrchestrationStatus>? statuses = filter.Statuses?.Select(x => x.ConvertToCore());
        return new Core.PurgeInstanceFilter(
            (filter.CreatedFrom ?? default).UtcDateTime, filter.CreatedTo?.UtcDateTime, statuses);
    }

    /// <summary>
    /// Convert <see cref="EntityId" /> to <see cref="EntityInstanceId" />.
    /// </summary>
    /// <param name="entityId">The entity ID to convert.</param>
    /// <returns>The converted entity instance ID.</returns>
    public static EntityInstanceId ConvertFromCore(this EntityId entityId)
        => new(entityId.Name, entityId.Key);

    /// <summary>
    /// Convert <see cref="EntityInstanceId" /> to <see cref="EntityId" />.
    /// </summary>
    /// <param name="entityId">The entity instance ID to convert.</param>
    /// <returns>The converted entity ID.</returns>
    public static EntityId ConvertToCore(this EntityInstanceId entityId)
        => new(entityId.Name, entityId.Key);
}
