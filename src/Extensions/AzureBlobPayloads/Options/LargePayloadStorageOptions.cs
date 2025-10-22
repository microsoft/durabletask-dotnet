// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;

// Intentionally no DataAnnotations to avoid extra package requirements in minimal hosts.
namespace Microsoft.DurableTask;

/// <summary>
/// Options for externalized payload storage, used by SDKs to store large payloads out-of-band.
/// Supports both connection string and identity-based authentication.
///
/// <example>
/// Connection string authentication:
/// <code>
/// var options = new LargePayloadStorageOptions("DefaultEndpointsProtocol=https;AccountName=mystorageaccount;AccountKey=...");
/// </code>
///
/// Identity-based authentication:
/// <code>
/// var options = new LargePayloadStorageOptions(
///     new Uri("https://mystorageaccount.blob.core.windows.net"),
///     new DefaultAzureCredential());
/// </code>
/// </example>
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
    /// Initializes a new instance of the <see cref="LargePayloadStorageOptions"/> class.
    /// </summary>
    /// <param name="accountUri">The Azure Storage account URI.</param>
    /// <param name="credential">The credential to use for authentication.</param>
    public LargePayloadStorageOptions(Uri accountUri, TokenCredential credential)
    {
        Check.NotNull(accountUri, nameof(accountUri));
        Check.NotNull(credential, nameof(credential));
        this.AccountUri = accountUri;
        this.Credential = credential;
    }

    /// <summary>
    /// Gets or sets the threshold in bytes at which payloads are externalized. Default is 900_000 bytes.
    /// </summary>
    public int ExternalizeThresholdBytes { get; set; } = 900_000;

    /// <summary>
    /// Gets or sets the maximum allowed size in bytes for any single externalized payload.
    /// Defaults to 10MB. Requests exceeding this limit will fail fast
    /// with a clear error to prevent unbounded payload growth and excessive storage/network usage.
    /// </summary>
    public int MaxExternalizedPayloadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the Azure Storage connection string to the customer's storage account.
    /// Either this or <see cref="AccountUri"/> and <see cref="Credential"/> must be set.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Azure Storage account URI.
    /// Either this and <see cref="Credential"/> or <see cref="ConnectionString"/> must be set.
    /// </summary>
    public Uri? AccountUri { get; set; }

    /// <summary>
    /// Gets or sets the credential to use for authentication.
    /// Either this and <see cref="AccountUri"/> or <see cref="ConnectionString"/> must be set.
    /// </summary>
    public TokenCredential? Credential { get; set; }

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
