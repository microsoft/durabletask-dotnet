// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask;
using Microsoft.DurableTask.AzureManaged.Internal;

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Options for declaring on-demand sandbox activities and the worker image DTS should start for them.
/// </summary>
public sealed class SandboxWorkerProfileOptions
{
    /// <summary>
    /// Default worker profile ID used when no profile is specified.
    /// </summary>
    internal const string DefaultWorkerProfileId = SandboxActivityMetadata.DefaultWorkerProfileId;

    /// <summary>
    /// Gets or sets the task hub where the on-demand sandbox activity declaration is stored.
    /// </summary>
    public string TaskHub { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the worker profile ID used for the on-demand sandbox activity pool.
    /// </summary>
    public string WorkerProfileId { get; set; } = DefaultWorkerProfileId;

    /// <summary>
    /// Gets or sets the full OCI container image reference for on-demand sandbox workers.
    /// Examples: <c>myregistry.azurecr.io/workers/hello:1.0</c> or
    /// <c>myregistry.azurecr.io/workers/hello@sha256:0123456789abcdef...</c>.
    /// </summary>
    public string? ContainerImage { get; set; }

    /// <summary>
    /// Gets or sets the user-assigned managed identity client ID ADC uses to pull the on-demand sandbox worker image.
    /// </summary>
    public string? ImagePullManagedIdentityClientId { get; set; }

    /// <summary>
    /// Gets or sets the user-assigned managed identity client ID workers use to authenticate to the DTS scheduler.
    /// </summary>
    public string? SchedulerManagedIdentityClientId { get; set; }

    /// <summary>
    /// Gets or sets the CPU quantity declared for each sandbox. Supported formats include <c>500m</c>, <c>2</c>, and <c>0.5</c>.
    /// </summary>
    public string Cpu { get; set; } = "1000m";

    /// <summary>
    /// Gets or sets the memory quantity declared for each sandbox. Supported formats include <c>256Mi</c>, <c>1Gi</c>, and <c>2048</c>.
    /// </summary>
    public string Memory { get; set; } = "2048Mi";

    /// <summary>
    /// Gets custom environment variables DTS should provide to on-demand sandbox workers created from this declaration.
    /// DTS-owned runtime variables such as <c>DTS_ENDPOINT</c>, <c>DTS_TASK_HUB</c>, and
    /// <c>DTS_SANDBOX_ID</c> are injected by the backend and should not be supplied here.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the sandbox entrypoint declared for on-demand sandbox workers.
    /// </summary>
    public IList<string> Entrypoint { get; } = new List<string>();

    /// <summary>
    /// Gets the sandbox command declared for on-demand sandbox workers.
    /// </summary>
    public IList<string> Cmd { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the maximum number of concurrent activities expected from each on-demand sandbox worker.
    /// </summary>
    public int MaxConcurrentActivities { get; set; } = 100;

    /// <summary>
    /// Gets the on-demand sandbox activity names to declare. Remote workers report their registered
    /// activities separately when they connect.
    /// </summary>
    internal IList<string> ActivityNames { get; } = new List<string>();

    /// <summary>
    /// Adds an activity name to the on-demand sandbox worker profile declaration.
    /// </summary>
    /// <param name="activityName">The activity name.</param>
    public void AddActivity(string activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            throw new ArgumentException("On-demand sandbox activity name cannot be empty.", nameof(activityName));
        }

        this.ActivityNames.Add(activityName.Trim());
    }

    /// <summary>
    /// Adds an activity to the on-demand sandbox worker profile declaration using its durable task name.
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
