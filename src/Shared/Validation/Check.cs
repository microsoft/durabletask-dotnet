// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

#nullable enable
namespace Microsoft.DurableTask;

/// <summary>
/// Helpers for <see cref="ArgumentException" /> assertions.
/// </summary>
static class Check
{
    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if the condition is false.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="message">The error message.</param>
    /// <param name="args">The message args for formatting.</param>
    public static void Argument(bool condition, string name, string message, params object[] args)
    {
        args ??= Array.Empty<object>();
        if (!condition)
        {
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, message, args), name);
        }
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException" /> if the provided value is the default value defined by
    /// <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The struct type.</typeparam>
    /// <param name="argument">The argument to check.</param>
    /// <param name="name">The name of the argument.</param>
    /// <returns>The provided argument.</returns>
    public static T NotDefault<T>(T argument, [CallerArgumentExpression("argument")] string? name = default)
        where T : struct
    {
        if (default(T).Equals(argument))
        {
            string message = $"Provided argument cannot be default value defined by '{typeof(T)}'";
            throw new ArgumentException(message, name);
        }

        return argument;
    }

    /// <summary>
    /// Checks in the provided element is null, throwing if it is.
    /// Throws <see cref="ArgumentException" /> if the conditions are not met.
    /// </summary>
    /// <param name="argument">The element to check.</param>
    /// <param name="name">The name of the element for the exception.</param>
    /// <typeparam name="T">The type of element to check.</typeparam>
    /// <returns>The original element.</returns>
    [return: NotNullIfNotNull("argument")]
    public static T NotNull<T>([NotNull] T? argument, [CallerArgumentExpression("argument")] string? name = default)
        where T : class
    {
        if (argument is null)
        {
            throw new ArgumentNullException(name);
        }

        return argument;
    }

    /// <summary>
    /// Checks in the provided string is null, throwing if it is.
    /// Throws <see cref="ArgumentException" /> if the conditions are not met.
    /// </summary>
    /// <param name="argument">The string to check.</param>
    /// <param name="name">The name of the string for the exception.</param>
    /// <returns>The original string.</returns>
    [return: NotNullIfNotNull("argument")]
    public static string NotNullOrEmpty(
        [NotNull] string? argument, [CallerArgumentExpression("argument")] string? name = default)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(name);
        }

        if (argument.Length == 0 || argument[0] == '\0')
        {
            throw new ArgumentException("Parameter cannot be an empty string or start with the null character.", name);
        }

        return argument;
    }

    /// <summary>
    /// Checks if the supplied type is a concrete non-abstract type and implements the provided generic type.
    /// Throws <see cref="ArgumentException" /> if the conditions are not met.
    /// </summary>
    /// <param name="argument">The type to check.</param>
    /// <param name="name">The name of the argument for the exception message.</param>
    /// <typeparam name="TImplements">The type <paramref name="argument" /> must implement.</typeparam>
    public static void ConcreteType<TImplements>(
        [NotNull] Type? argument, [CallerArgumentExpression("argument")] string? name = default)
    {
        NotNull(argument, name);
        if (!typeof(TImplements).IsAssignableFrom(argument) || !argument.IsClass || argument.IsAbstract)
        {
            string message = $"The provided type ['{argument}'] is invalid. The provided type must inherit from"
                + $" '{typeof(TImplements)}', be a class, and not be abstract.";
            throw new ArgumentException(message, name);
        }
    }

    /// <summary>
    /// Checks if the supplied type is an interface.
    /// Throws <see cref="ArgumentException" /> if the conditions are not met.
    /// </summary>
    /// <param name="argument">The type to check.</param>
    /// <param name="name">The name of the argument for the exception message.</param>
    public static void IsInterface(
        [NotNull] Type? argument, [CallerArgumentExpression("argument")] string? name = default)
    {
        NotNull(argument, name);
        if (!argument.IsInterface)
        {
            string message = $"The provided type ['{argument}'] is invalid. The provided type must be an interface.";
            throw new ArgumentException(message, name);
        }
    }
}
