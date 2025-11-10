// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Priority level enum for intuitive priority specification.
/// </summary>
public enum OrchestrationPriorityLevel
{
    /// <summary>
    /// Unspecified priority. Uses instance key (normal priority, FIFO).
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Highest priority (priority value 0).
    /// </summary>
    Urgent = 1,

    /// <summary>
    /// High priority (priority value 1000).
    /// </summary>
    High = 2,

    /// <summary>
    /// Normal priority (uses instance key, FIFO).
    /// </summary>
    Normal = 3,

    /// <summary>
    /// Low priority (instance key + 1,000,000).
    /// </summary>
    Low = 4,

    /// <summary>
    /// Background priority (instance key + 10,000,000).
    /// </summary>
    Background = 5,
}

