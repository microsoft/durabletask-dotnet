// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask;

/// <summary>
/// Record that represents the details of an orchestration instance failure.
/// </summary>
/// <param name="ErrorName">The name of the error type. For .NET, this is the namespace-qualified exception type name.</param>
/// <param name="ErrorMessage">A summary description of the failure.</param>
/// <param name="ErrorDetails">The full details of the failure, which often includes an exception call-stack.</param>
public record OrchestrationFailureDetails(string ErrorName, string ErrorMessage, string? ErrorDetails);
