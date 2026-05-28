// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Options for declaring serverless activities and the worker image DTS should start for them.
/// </summary>
public sealed class ServerlessOptions
{
    /// <summary>
    /// Default worker profile ID used when no profile is specified.
    /// </summary>
    internal const string DefaultWorkerProfileId = "default";

    /// <summary>
    /// Gets or sets the task hub where the serverless activity declaration is stored.
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
    /// Gets or sets the CPU quantity declared for each serverless sandbox.
    /// </summary>
    public string Cpu { get; set; } = "1000m";

    /// <summary>
    /// Gets or sets the memory quantity declared for each serverless sandbox.
    /// </summary>
    public string Memory { get; set; } = "2048Mi";

    /// <summary>
    /// Gets custom environment variables DTS should provide to serverless workers created from this declaration.
    /// DTS-owned runtime variables such as <c>DTS_ENDPOINT</c>, <c>DTS_TASK_HUB</c>, and
    /// <c>DTS_SANDBOX_ID</c> are injected by the backend and should not be supplied here.
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
    /// Gets or sets the maximum number of concurrent activities expected from each serverless worker.
    /// </summary>
    public int MaxConcurrentActivities { get; set; } = 100;

    /// <summary>
    /// Gets the serverless activity names to declare. Remote workers report their registered
    /// activities separately when they connect.
    /// </summary>
    internal IList<string> ActivityNames { get; } = new List<string>();

    /// <summary>
    /// Adds an activity name to the serverless worker profile declaration.
    /// </summary>
    /// <param name="activityName">The activity name.</param>
    public void AddActivity(string activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            throw new ArgumentException("Serverless activity name cannot be empty.", nameof(activityName));
        }

        this.ActivityNames.Add(activityName.Trim());
    }

    /// <summary>
    /// Adds an activity to the serverless worker profile declaration using its durable task name.
    /// </summary>
    /// <typeparam name="TActivity">The activity type.</typeparam>
    public void AddActivity<TActivity>() where TActivity : class, ITaskActivity
    {
        Type activityType = typeof(TActivity);
        DurableTaskAttribute? attribute = activityType.GetCustomAttribute<DurableTaskAttribute>();
        string? activityName = attribute?.Name.Name;
        this.AddActivity(string.IsNullOrWhiteSpace(activityName) ? activityType.Name : activityName);
    }
}
