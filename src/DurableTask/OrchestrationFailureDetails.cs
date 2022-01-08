// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask;

/// <summary>
/// Record that represents the details of an orchestration instance failure.
/// </summary>
/// <param name="Message">A summary description of the failure.</param>
/// <param name="Details">The full details of the failure, which is often an exception call-stack.</param>
public record OrchestrationFailureDetails(string Message, string? Details);
