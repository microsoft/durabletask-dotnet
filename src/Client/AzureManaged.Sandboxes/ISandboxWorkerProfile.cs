// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.AzureManaged;

/// <summary>
/// Configures an on-demand sandbox worker profile workerProfile.
/// </summary>
public interface ISandboxWorkerProfile
{
    /// <summary>
    /// Configures the on-demand sandbox worker profile workerProfile options.
    /// </summary>
    /// <param name="options">The workerProfile options to configure.</param>
    void Configure(SandboxWorkerProfileOptions options);
}
