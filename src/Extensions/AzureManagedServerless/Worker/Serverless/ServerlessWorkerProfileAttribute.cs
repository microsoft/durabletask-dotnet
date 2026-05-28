// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Declares a serverless worker profile that DTS can start for activities declared by the profile.
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
