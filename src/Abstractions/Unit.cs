// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents a <see cref="Void" /> result.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
#pragma warning disable CA1801 // unused parameters
#pragma warning disable IDE0060 // unused parameters

    static readonly Unit RefValue;

    /// <summary>
    /// Gets the default value for <see cref="Unit" />.
    /// </summary>
    public static ref readonly Unit Value => ref RefValue;

    /// <summary>
    /// Gets the task result for a <see cref="Unit" />.
    /// </summary>
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(RefValue);

    /// <summary>
    /// Compares two empties for equality. Always true.
    /// </summary>
    /// <param name="left">The left empty.</param>
    /// <param name="right">The right empty.</param>
    /// <returns>Always true.</returns>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>
    /// Compares two empties for inequality. Always false.
    /// </summary>
    /// <param name="left">The left empty.</param>
    /// <param name="right">The right empty.</param>
    /// <returns>Always false.</returns>
    public static bool operator !=(Unit left, Unit right) => !true;

    /// <summary>
    /// Compares two empties. Always false.
    /// </summary>
    /// <param name="left">The left empty.</param>
    /// <param name="right">The right empty.</param>
    /// <returns>Always false.</returns>
    public static bool operator <(Unit left, Unit right) => false;

    /// <summary>
    /// Compares two empties. Always true.
    /// </summary>
    /// <param name="left">The left empty.</param>
    /// <param name="right">The right empty.</param>
    /// <returns>Always true.</returns>
    public static bool operator <=(Unit left, Unit right) => true;

    /// <summary>
    /// Compares two empties. Always false.
    /// </summary>
    /// <param name="left">The left empty.</param>
    /// <param name="right">The right empty.</param>
    /// <returns>Always false.</returns>
    public static bool operator >(Unit left, Unit right) => false;

    /// <summary>
    /// Compares two empties. Always true.
    /// </summary>
    /// <param name="left">The left empty.</param>
    /// <param name="right">The right empty.</param>
    /// <returns>Always true.</returns>
    public static bool operator >=(Unit left, Unit right) => true;

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public override string ToString() => "{}";

#pragma warning restore CA1801 // unused parameters
#pragma warning restore IDE0060 // unused parameters
}
