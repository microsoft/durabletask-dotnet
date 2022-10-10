// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Initializes a new instance of the <see cref="OrchestrationInvocationContext"/> class.
/// </summary>
/// <param name="Name">The invoked orchestration name.</param>
/// <param name="Options">The Durable Task worker options.</param>
/// <param name="LoggerFactory">The logger factory for this orchestration.</param>
/// <param name="Parent">The orchestration parent details.</param>
record OrchestrationInvocationContext(
    TaskName Name,
    DurableTaskWorkerOptions Options,
    ILoggerFactory LoggerFactory,
    ParentOrchestrationInstance? Parent = null);
