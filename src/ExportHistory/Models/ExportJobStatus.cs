// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Represents the current status of an export history job.
/// </summary>
public enum ExportJobStatus
{
    /// <summary>
    /// Export history job has been created but is not yet active.
    /// </summary>
    Pending,

    /// <summary>
    /// Export history job is active and running.
    /// </summary>
    Active,

    /// <summary>
    /// Export history job failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Export history job completed.
    /// </summary>
    Completed,
}
