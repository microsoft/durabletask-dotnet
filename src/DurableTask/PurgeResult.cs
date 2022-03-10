// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask;

/// <summary>
/// Results of a purge operation.
/// </summary>
public class PurgeResult
{
    internal PurgeResult(int count)
    {
        this.PurgedInstanceCount = count;
    }

    /// <summary>
    /// Gets the number of purged instances.
    /// </summary>
    /// <value>The number of purged instances.</value>
    public int PurgedInstanceCount { get; }
}
