// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;

/// <summary>
/// Configures an on-demand sandbox worker profile declaration.
/// </summary>
public interface ISandboxWorkerProfile
{
    /// <summary>
    /// Configures the on-demand sandbox worker profile declaration options.
    /// </summary>
    /// <param name="options">The declaration options to configure.</param>
    void Configure(OnDemandSandboxOptions options);
}
