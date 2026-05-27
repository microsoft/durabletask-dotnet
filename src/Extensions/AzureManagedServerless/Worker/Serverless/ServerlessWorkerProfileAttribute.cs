// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Declares a serverless worker profile that DTS can start for annotated activities.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ServerlessWorkerProfileAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessWorkerProfileAttribute"/> class.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID.</param>
    public ServerlessWorkerProfileAttribute(string workerProfileId)
    {
        this.WorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
            ? throw new ArgumentException("Serverless worker profile ID cannot be empty.", nameof(workerProfileId))
            : workerProfileId.Trim();
    }

    /// <summary>
    /// Gets the worker profile ID.
    /// </summary>
    public string WorkerProfileId { get; }
}

/// <summary>
/// Declares that an activity should run on a DTS-managed serverless worker profile.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ServerlessActivityAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the activity name. If not specified, the annotated class name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the worker profile ID that owns this serverless activity.
    /// </summary>
    public string WorkerProfile { get; set; } = ServerlessOptions.DefaultWorkerProfileId;
}
