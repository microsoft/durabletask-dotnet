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
    /// Initializes a new instance of the <see cref="PurgeResult" /> class.
    /// </summary>
    /// <param name="count">The number of instances deleted.</param>
    /// <param name="isComplete">A value indicating whether the purge operation is complete.
    /// If true, the purge operation is complete. All instances were purged.
    /// If false, not all instances were purged. Please purge again.
    /// If null, whether or not all instances were purged is undefined.</param>
    public PurgeResult(int count, bool? isComplete)
        : this(count)
    {
        this.IsComplete = isComplete;
    }

    /// <summary>
    /// Gets the number of purged instances.
    /// </summary>
    /// <value>The number of purged instances.</value>
    public int PurgedInstanceCount { get; }

    /// <summary>
    /// Gets a value indicating whether the purge operation is complete.
    /// </summary>
    /// <value>A value indicating whether the purge operation is complete.</value>
    public bool? IsComplete { get; }
}
