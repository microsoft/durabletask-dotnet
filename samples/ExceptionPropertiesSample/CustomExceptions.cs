// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace ExceptionPropertiesSample;

/// <summary>
/// Custom business exception that includes additional properties for better error diagnostics.
/// </summary>
public class BusinessValidationException : Exception
{
    public BusinessValidationException(
        string message,
        string? errorCode = null,
        int? statusCode = null,
        Dictionary<string, object?>? metadata = null)
        : base(message)
    {
        this.ErrorCode = errorCode;
        this.StatusCode = statusCode;
        this.Metadata = metadata ?? new Dictionary<string, object?>();
    }

    /// <summary>
    /// Gets the error code associated with this validation failure.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the HTTP status code that should be returned for this error.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Gets additional metadata about the validation failure.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; }
}

