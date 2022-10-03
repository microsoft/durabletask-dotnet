// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Initializes a new instance of the <see cref="OrchestrationInvocationContext"/> class.
/// </summary>
/// <param name="Name">The invoked orchestration name.</param>
/// <param name="DataConverter">The data converter for this orchestration.</param>
/// <param name="LoggerFactory">The logger factory for this orchestration.</param>
/// <param name="TimerOptions">The configuration options for durable timers.</param>
/// <param name="Parent">The orchestration parent details.</param>
record OrchestrationInvocationContext(
    TaskName Name,
    DataConverter DataConverter,
    ILoggerFactory LoggerFactory,
    TimerOptions TimerOptions,
    ParentOrchestrationInstance? Parent = null);