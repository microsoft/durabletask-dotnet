// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Export destination settings for Azure Blob Storage.
/// </summary>
public sealed class ExportDestination
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExportDestination"/> class.
    /// </summary>
    public ExportDestination()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportDestination"/> class.
    /// </summary>
    /// <param name="container">The blob container name.</param>
    /// <exception cref="ArgumentException">Thrown when container is null or empty.</exception>
    public ExportDestination(string container)
    {
        Check.NotNullOrEmpty(container, nameof(container));
        this.Container = container;
    }

    /// <summary>
    /// Gets or sets the blob container name.
    /// </summary>
    public string Container { get; set; }

    /// <summary>
    /// Gets or sets an optional prefix for blob paths.
    /// </summary>
    public string? Prefix { get; set; }
}
