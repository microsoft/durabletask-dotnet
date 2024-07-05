// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Sidecar.App;

/// <summary>
/// Represents the supported Durable Task storage provider backends.
/// </summary>
enum BackendType
{
    AzureStorage,
    MSSQL,
    Netherite,
    Emulator,
}
