// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Intentionally no DataAnnotations to avoid extra package requirements in minimal hosts.
namespace Microsoft.DurableTask.Converters;

/// <summary>
/// Options for externalized payload storage, used by SDKs to store large payloads out-of-band.
/// </summary>
public sealed class LargePayloadStorageOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LargePayloadStorageOptions"/> class.
    /// Parameterless constructor required for options activation.
    /// </summary>
    public LargePayloadStorageOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LargePayloadStorageOptions"/> class.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string to the customer's storage account.</param>
    public LargePayloadStorageOptions(string connectionString)
    {
        Check.NotNullOrEmpty(connectionString, nameof(connectionString));
        this.ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets or sets the threshold in bytes at which payloads are externalized. Default is 900_000 bytes.
    /// </summary>
    public int ExternalizeThresholdBytes { get; set; } = 900_000; // leave headroom below 1MB

    /// <summary>
    /// Gets or sets the Azure Storage connection string to the customer's storage account. Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the blob container name to use for payloads. Defaults to "durabletask-payloads".
    /// </summary>
    public string ContainerName { get; set; } = "durabletask-payloads";

    /// <summary>
    /// Gets or sets a value indicating whether payloads should be gzip-compressed when stored.
    /// Defaults to true for reduced storage and bandwidth.
    /// </summary>
    public bool CompressPayloads { get; set; } = true;
}
