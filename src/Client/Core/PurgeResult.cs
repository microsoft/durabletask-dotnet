// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Results of a purge operation.
/// </summary>
public class PurgeResult
{
    public PurgeResult(int count)
    {
        this.PurgedInstanceCount = count;
    }

    /// <summary>
    /// Gets the number of purged instances.
    /// </summary>
    /// <value>The number of purged instances.</value>
    public int PurgedInstanceCount { get; }
}
