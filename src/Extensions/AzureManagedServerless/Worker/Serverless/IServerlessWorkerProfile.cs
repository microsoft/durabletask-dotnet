// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Configures a serverless worker profile declaration.
/// </summary>
public interface IServerlessWorkerProfile
{
    /// <summary>
    /// Configures the serverless worker profile declaration options.
    /// </summary>
    /// <param name="options">The declaration options to configure.</param>
    void Configure(ServerlessOptions options);
}
