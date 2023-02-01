// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Results of a purge operation.
/// </summary>
public class PurgeResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PurgeResult" /> class.
    /// </summary>
    /// <param name="count">The count of instances purged.</param>
    public PurgeResult(int count)
    {
        Check.Argument(count >= 0, nameof(count), "Count must be non-negative");
        this.PurgedInstanceCount = count;
    }

    /// <summary>
    /// Gets the number of purged instances.
    /// </summary>
    /// <value>The number of purged instances.</value>
    public int PurgedInstanceCount { get; }
}
