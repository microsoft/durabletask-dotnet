// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Defines how a worker participates in serverless activity execution.
/// </summary>
internal enum ServerlessMode
{
    /// <summary>
    /// The local worker declares serverless activities and excludes them from local execution.
    /// </summary>
    LocalExclude,

    /// <summary>
    /// The worker runs inside serverless infrastructure and executes only serverless activities.
    /// </summary>
    ServerlessInclude,
}

/// <summary>
/// Options for configuring serverless activity worker behavior.
/// </summary>
public sealed class ServerlessOptions
{
    /// <summary>
    /// Default worker profile ID used when no profile is specified.
    /// </summary>
    internal const string DefaultWorkerProfileId = "default";

    /// <summary>
    /// Gets the worker mode for serverless activity execution. Set automatically from the runtime environment.
    /// </summary>
    internal ServerlessMode Mode { get; set; } = ServerlessMode.LocalExclude;

    /// <summary>
    /// Gets the serverless activity names to declare or execute.
    /// </summary>
    public IList<string> ActivityNames { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the task hub used by serverless activity calls.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the worker profile ID used for the serverless activity pool.
    /// </summary>
    public string WorkerProfileId { get; set; } = DefaultWorkerProfileId;

    /// <summary>
    /// Gets or sets the full container image reference for serverless workers.
    /// </summary>
    public string? ContainerImage { get; set; }

    /// <summary>
    /// Gets or sets the registry server for the serverless worker image.
    /// </summary>
    public string? RegistryServer { get; set; }

    /// <summary>
    /// Gets or sets the repository for the serverless worker image.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the tag for the serverless worker image.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Gets or sets the digest for the serverless worker image.
    /// </summary>
    public string? ImageDigest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the image is publicly pullable. Private preview requires this to be <c>true</c>.
    /// </summary>
    public bool PublicPull { get; set; } = true;

    /// <summary>
    /// Gets or sets the CPU quantity declared for each serverless sandbox.
    /// </summary>
    public string Cpu { get; set; } = "1000m";

    /// <summary>
    /// Gets or sets the memory quantity declared for each serverless sandbox.
    /// </summary>
    public string Memory { get; set; } = "2048Mi";

    /// <summary>
    /// Gets environment variables DTS should provide to serverless workers created from this declaration.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the sandbox entrypoint declared for serverless workers.
    /// </summary>
    public IList<string> Entrypoint { get; } = new List<string>();

    /// <summary>
    /// Gets the sandbox command declared for serverless workers.
    /// </summary>
    public IList<string> Cmd { get; } = new List<string>();

    /// <summary>
    /// Gets the unique worker instance identifier.
    /// </summary>
    public string WorkerInstanceId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the maximum number of concurrent activities expected from each serverless worker.
    /// </summary>
    public int MaxConcurrentActivities { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of declaration attempts made on transient failures.
    /// </summary>
    public int DeclarationRetryMaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the delay between declaration retry attempts.
    /// </summary>
    public TimeSpan DeclarationRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the interval used to refresh live worker capacity while the registration stream is open.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);
}
