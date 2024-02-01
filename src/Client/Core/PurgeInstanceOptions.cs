// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Options to purge an orchestration.
/// </summary>
public record PurgeInstanceOptions
{
    /// <summary>
    /// Gets a value indicating whether to purge sub-orchestrations as well.
    /// </summary>
    public bool Recursive { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PurgeInstanceOptions"/> class.
    /// </summary>
    /// <param name="recursive">The optional boolean value indicating whether to purge sub-orchestrations as well.</param>
    public PurgeInstanceOptions(bool recursive = true)
    {
        this.Recursive = recursive;
    }
}