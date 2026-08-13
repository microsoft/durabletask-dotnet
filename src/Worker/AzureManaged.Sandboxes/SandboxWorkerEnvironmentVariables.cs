// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;

/// <summary>
/// Environment variable names injected into on-demand sandbox workers by DTS.
/// </summary>
static class SandboxWorkerEnvironmentVariables
{
    /// <summary>The scheduler endpoint used by the sandbox worker.</summary>
    public const string Endpoint = "DTS_ENDPOINT";

    /// <summary>The task hub used by the sandbox worker.</summary>
    public const string TaskHub = "DTS_TASK_HUB";

    /// <summary>The authentication mode used by the sandbox worker.</summary>
    public const string Authentication = "DTS_AUTHENTICATION";

    /// <summary>The user-assigned managed identity client ID used by the sandbox worker to connect to DTS.</summary>
    public const string ManagedIdentityClientId = "DTS_UMI_CLIENT_ID";

    /// <summary>The sandbox provider kind used for worker registration.</summary>
    public const string SandboxProvider = "DTS_SANDBOX_PROVIDER";

    /// <summary>The worker profile ID used for worker registration.</summary>
    public const string WorkerProfileId = "DTS_WORKER_PROFILE_ID";

    /// <summary>The maximum number of concurrent activities for this sandbox worker.</summary>
    public const string MaxActivities = "DTS_SANDBOX_MAX_ACTIVITIES";

    /// <summary>The sandbox instance identifier used for worker registration.</summary>
    public const string SandboxId = "DTS_SANDBOX_ID";
}
