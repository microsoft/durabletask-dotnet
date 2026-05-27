// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Marks an activity as serverless and associates it with a serverless worker profile.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ServerlessActivityAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerlessActivityAttribute"/> class.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID that owns this activity.</param>
    public ServerlessActivityAttribute(string workerProfileId)
    {
        this.WorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
            ? throw new ArgumentException("Serverless activity worker profile ID cannot be empty.", nameof(workerProfileId))
            : workerProfileId.Trim();
    }

    /// <summary>
    /// Gets the worker profile ID that owns this activity.
    /// </summary>
    public string WorkerProfileId { get; }

    /// <summary>
    /// Gets or sets the activity name when this attribute is applied to a declaration marker class.
    /// </summary>
    public string? Name { get; set; }
}
