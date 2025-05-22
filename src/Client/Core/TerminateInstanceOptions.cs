// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Client;

/// <summary>
///  Options to terminate an orchestration.
/// </summary>
/// <param name="Output">The optional output to set for the terminated orchestration instance.</param>
/// <param name="Recursive">The optional boolean value indicating whether to terminate sub-orchestrations as well.</param>
public record TerminateInstanceOptions(object? Output = null, bool Recursive = false);
