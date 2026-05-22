// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Defines how a worker participates in serverless activity execution.
/// </summary>
internal enum ServerlessMode
{
    /// <summary>
    /// The worker is not running inside serverless infrastructure.
    /// </summary>
    LocalExclude,

    /// <summary>
    /// The worker runs inside serverless infrastructure and executes only serverless activities.
    /// </summary>
    ServerlessInclude,
}

/// <summary>
/// Internal runtime settings for a sandbox serverless worker process.
/// </summary>
internal sealed class ServerlessWorkerRuntimeOptions
{
    /// <summary>
    /// Gets or sets the task hub used by serverless worker registration.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the worker profile ID used by serverless worker registration.
    /// </summary>
    public string WorkerProfileId { get; set; } = ServerlessOptions.DefaultWorkerProfileId;

    /// <summary>
    /// Gets or sets the maximum number of concurrent activities expected from this serverless worker.
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
    /// Gets or sets the worker mode for serverless activity execution. Set automatically from the runtime environment.
    /// </summary>
    public ServerlessMode Mode { get; set; } = ServerlessMode.LocalExclude;
}
