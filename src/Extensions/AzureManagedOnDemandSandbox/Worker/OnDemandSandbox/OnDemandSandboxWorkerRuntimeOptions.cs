// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Defines how a worker participates in on-demand sandbox activity execution.
/// </summary>
internal enum OnDemandSandboxMode
{
    /// <summary>
    /// The worker is not running inside on-demand sandbox infrastructure.
    /// </summary>
    LocalExclude,

    /// <summary>
    /// The worker runs inside on-demand sandbox infrastructure and executes only on-demand sandbox activities.
    /// </summary>
    OnDemandSandboxInclude,
}

/// <summary>
/// Internal runtime settings for an on-demand sandbox worker process.
/// </summary>
internal sealed class OnDemandSandboxWorkerRuntimeOptions
{
    /// <summary>
    /// Gets or sets the task hub used by on-demand sandbox worker registration.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the worker profile ID used by on-demand sandbox worker registration.
    /// </summary>
    public string WorkerProfileId { get; set; } = OnDemandSandboxOptions.DefaultWorkerProfileId;

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

    /// <summary>
    /// Gets or sets the worker mode for on-demand sandbox activity execution. Set automatically from the runtime environment.
    /// </summary>
    public OnDemandSandboxMode Mode { get; set; } = OnDemandSandboxMode.LocalExclude;
}
