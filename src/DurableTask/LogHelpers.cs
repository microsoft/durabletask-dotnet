// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Non-generated helpers for <see cref="ILogger" />.
/// </summary>
static class LogHelpers
{
    public static void PurgingInstances(this ILogger logger, PurgeInstancesFilter filter)
    {
        string? statuses = filter?.Statuses is null ? null : string.Join("|", filter.Statuses);
        Logs.PurgingInstances(logger, filter?.CreatedFrom, filter?.CreatedTo, statuses);
    }
}
