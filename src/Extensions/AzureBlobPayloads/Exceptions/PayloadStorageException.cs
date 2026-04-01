// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Exception thrown when a payload storage operation fails permanently.
/// </summary>
public sealed class PayloadStorageException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStorageException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PayloadStorageException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PayloadStorageException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PayloadStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
