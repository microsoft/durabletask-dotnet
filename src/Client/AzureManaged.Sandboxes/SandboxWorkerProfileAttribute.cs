// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Declares an on-demand sandbox worker profile that DTS can start for activities declared by the profile.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SandboxWorkerProfileAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxWorkerProfileAttribute"/> class.
    /// </summary>
    /// <param name="workerProfileId">The worker profile ID.</param>
    public SandboxWorkerProfileAttribute(string workerProfileId)
    {
        this.WorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
            ? throw new ArgumentException("On-demand sandbox worker profile ID cannot be empty.", nameof(workerProfileId))
            : workerProfileId.Trim();
    }

    /// <summary>
    /// Gets the worker profile ID.
    /// </summary>
    public string WorkerProfileId { get; }
}
