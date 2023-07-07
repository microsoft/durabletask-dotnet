// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Entity signalling options.
/// </summary>
public record SignalEntityOptions
{
    /// <summary>
    /// Gets the time to signal the entity at.
    /// </summary>
    public DateTimeOffset? SignalTime { get; init; }
}
