// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Options for Azure Storage configuration for export history jobs.
/// Supports connection string-based authentication.
/// </summary>
public sealed class ExportHistoryStorageOptions
{
    /// <summary>
    /// Gets or sets the Azure Storage connection string to the customer's storage account.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the blob container name where export data will be stored.
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional prefix for blob paths.
    /// </summary>
    public string? Prefix { get; set; }
}

