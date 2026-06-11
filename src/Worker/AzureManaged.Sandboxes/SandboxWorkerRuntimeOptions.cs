// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AzureManaged.Internal;

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Internal runtime settings for an on-demand sandbox worker process.
/// </summary>
internal sealed class SandboxWorkerRuntimeOptions
{
    /// <summary>
    /// Gets or sets the task hub used by on-demand sandbox worker registration.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the worker profile ID used by on-demand sandbox worker registration.
    /// </summary>
    public string WorkerProfileId { get; set; } = SandboxActivityMetadata.DefaultWorkerProfileId;

    /// <summary>
    /// Gets or sets the maximum number of concurrent activities expected from this on-demand sandbox worker.
    /// </summary>
    public int MaxConcurrentActivities { get; set; } = 100;

    /// <summary>
    /// Gets or sets the interval used to refresh live worker capacity while the registration stream is open.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the initial delay before retrying a failed worker registration stream.
    /// </summary>
    public TimeSpan WorkerRegistrationRetryInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay before retrying a failed worker registration stream.
    /// </summary>
    public TimeSpan WorkerRegistrationRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
}
