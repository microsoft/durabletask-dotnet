// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Options to terminate an orchestration.
/// </summary>
public record TerminateInstanceOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TerminateInstanceOptions"/> class.
    /// </summary>
    /// <param name="output">The optional output to set for the terminated orchestration instance.</param>
    /// <param name="recursive">The optional boolean value indicating whether to terminate sub-orchestrations as well.</param>
    public TerminateInstanceOptions(object? output = null, bool recursive = true)
    {
        this.Output = output;
        this.Recursive = recursive;
    }

    /// <summary>
    /// Gets the optional output to set for the terminated orchestration instance.
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// Gets a value indicating whether to terminate sub-orchestrations as well.
    /// </summary>
    public bool Recursive { get; init; }
}
