// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Options for a sandbox worker that registers live remote activity capacity with DTS.
/// </summary>
public sealed class RemoteActivityWorkerOptions
{
    /// <summary>
    /// Gets the remote activity names this worker should execute.
    /// </summary>
    public IList<string> ActivityNames { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the task hub this worker connects to.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the worker profile ID this worker registers capacity for.
    /// </summary>
    public string WorkerProfileId { get; set; } = RemoteActivityOptions.DefaultWorkerProfileId;

    /// <summary>
    /// Gets the unique worker instance identifier.
    /// </summary>
    public string WorkerInstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the maximum number of concurrent activities this worker can accept.
    /// </summary>
    public int MaxConcurrentActivities { get; set; } = 100;

    /// <summary>
    /// Gets or sets the interval used to refresh live worker capacity while the registration stream is open.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);
}
