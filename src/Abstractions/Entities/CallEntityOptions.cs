// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Entity calling options.
/// </summary>
public record CallEntityOptions
{
    /// <summary>
    /// Gets options indicating whether to signal the entity or not.
    /// </summary>
    /// <remarks>
    /// Setting this to non-<c>null</c> will signal the entity without waiting for a response.
    /// </remarks>
    /// <example>
    /// Signal without start time:
    /// <code>new CallEntityOptions { Signal = true };</code>
    /// </example>
    /// <example>
    /// Signal with start time:
    /// <code>new CallEntityOptions { Signal = DateTimeOffset };</code>
    /// </example>
    public SignalEntityOptions? Signal { get; init; }
}

/// <summary>
/// Entity signalling options.
/// </summary>
public record SignalEntityOptions
{
    /// <summary>
    /// Gets the time to signal the entity at.
    /// </summary>
    public DateTimeOffset? SignalTime { get; init; }

    /// <summary>
    /// Implicitly creates a <see cref="SignalEntityOptions"/> from a <see cref="bool"/>.
    /// </summary>
    /// <param name="value">The <see cref="bool"/> to convert from.</param>
    /// <remarks>
    /// This allows for expressing <see cref="CallEntityOptions"/> as:
    /// <code>new CallEntityOptions { Signal = true };</code>
    /// </remarks>
    public static implicit operator SignalEntityOptions?(bool? value)
        => value == true ? new() : null;

    /// <summary>
    /// Implicitly creates a <see cref="SignalEntityOptions"/> from a <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <param name="value">The <see cref="DateTimeOffset"/> to convert from.</param>
    /// <remarks>
    /// This allows for expressing <see cref="CallEntityOptions"/> as:
    /// <code>new CallEntityOptions { Signal = DateTimeOffset };</code>
    /// </remarks>
    public static implicit operator SignalEntityOptions(DateTimeOffset value)
        => new() { SignalTime = value };
}
