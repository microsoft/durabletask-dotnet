// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// Result of an input validation check.
/// </summary>
public readonly struct ValidationResult
{
    /// <summary>
    /// A successful validation result.
    /// </summary>
    public static readonly ValidationResult Success = new(true, null);

    ValidationResult(bool isValid, string? errorMessage)
    {
        this.IsValid = isValid;
        this.ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets a value indicating whether the validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A failed validation result.</returns>
    public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);
}
