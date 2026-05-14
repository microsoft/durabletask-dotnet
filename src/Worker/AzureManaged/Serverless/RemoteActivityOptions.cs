// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Options for declaring remote activities and the image DTS should use to run them.
/// </summary>
public sealed class RemoteActivityOptions
{
    /// <summary>
    /// Default worker profile ID used when no profile is specified.
    /// </summary>
    internal const string DefaultWorkerProfileId = "default";

    /// <summary>
    /// Gets the remote activity names to declare.
    /// </summary>
    public IList<string> ActivityNames { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the task hub that owns this declaration.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full container image reference for the remote worker image.
    /// </summary>
    public string? ContainerImage { get; set; }

    /// <summary>
    /// Gets or sets the registry server for the remote worker image.
    /// </summary>
    public string? RegistryServer { get; set; }

    /// <summary>
    /// Gets or sets the repository for the remote worker image.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the tag for the remote worker image.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Gets or sets the digest for the remote worker image.
    /// </summary>
    public string? ImageDigest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the image is publicly pullable. Private preview requires this to be <c>true</c>.
    /// </summary>
    public bool PublicPull { get; set; } = true;

    /// <summary>
    /// Gets environment variables DTS should provide to remote workers created from this declaration.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the maximum concurrent activities expected from each remote worker.
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
}
