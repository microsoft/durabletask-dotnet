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
    int thresholdBytes = 256 * 1024;

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
    /// Gets or sets the threshold in bytes at which payloads are externalized. Default is 256 KiB (262,144 bytes).
    /// Value must not exceed 1 MiB (1,048,576 bytes).
    /// </summary>
    public int ThresholdBytes
    {
        get => this.thresholdBytes;
        set
        {
            const int OneMiB = 1 * 1024 * 1024;
            if (value > OneMiB)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(this.ThresholdBytes),
                    $"Payload storage threshold cannot exceed 1 MiB ({OneMiB} bytes).");
            }

            this.thresholdBytes = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum allowed size in bytes for any single externalized payload.
    /// Defaults to 10MB. Requests exceeding this limit will fail fast
    /// with a clear error to prevent unbounded payload growth and excessive storage/network usage.
    /// </summary>
    public int MaxPayloadBytes { get; set; } = 10 * 1024 * 1024;

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
    /// <remarks>
    /// Uploads always use this container. Downloads and deletion are also restricted to this container
    /// unless an account-wide scope is explicitly configured. Before changing locations, complete or
    /// migrate existing instances and clean up their payloads, or explicitly authorize the original
    /// location using <see cref="AllowAllContainersInConfiguredAccount"/> or <see cref="AdditionalAccountUris"/>.
    /// </remarks>
    public string ContainerName { get; set; } = "durabletask-payloads";

    /// <summary>
    /// Gets or sets a value indicating whether payload downloads and deletion may access all containers
    /// in the configured storage account. Defaults to false.
    /// </summary>
    /// <remarks>
    /// This relaxes only the container restriction, not the scheme, host, port, or path-style account boundary.
    /// Operations retain the configured identity, account key, or SAS and its existing Storage permissions.
    /// A container-scoped SAS is not granted access to other containers by enabling this option.
    /// The scope is captured when the payload store is constructed.
    /// </remarks>
    public bool AllowAllContainersInConfiguredAccount { get; set; }

    /// <summary>
    /// Gets the additional storage account endpoints whose containers may be accessed for payload downloads
    /// and deletion. The default collection is empty.
    /// </summary>
    /// <remarks>
    /// Each entry must be an absolute HTTPS account endpoint without a container, blob, user information,
    /// query, or fragment. Scheme, host, effective port, and path-style account name must match exactly;
    /// entries are not hostname-only permissions. All containers in each listed additional account are allowed.
    /// This requires <see cref="AccountUri"/> and <see cref="Credential"/> authentication and reuses that
    /// credential without falling back to connection-string authentication. Storage permissions still apply.
    /// Entries equivalent to the configured account are rejected; use
    /// <see cref="AllowAllContainersInConfiguredAccount"/> for that account. Equivalent additional entries
    /// are deduplicated.
    /// Endpoints are captured when the payload store is constructed; reconstruct the store to change them.
    /// </remarks>
    public IList<Uri> AdditionalAccountUris { get; } = new List<Uri>();

    /// <summary>
    /// Gets or sets a value indicating whether payloads should be gzip-compressed when stored.
    /// Defaults to true for reduced storage and bandwidth.
    /// </summary>
    public bool CompressionEnabled { get; set; } = true;
}
