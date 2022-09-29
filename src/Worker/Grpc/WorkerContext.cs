// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Initializes a new instance of the <see cref="WorkerContext"/> class.
/// </summary>
/// <param name="DataConverter">The data converter to use for serializing and deserializing payloads.</param>
/// <param name="Logger">The logger to use for emitting logs.</param>
/// <param name="Services">The dependency-injection service provider.</param>
/// <param name="TimerOptions">Optional. The configuration options for durable timers.</param>
record WorkerContext(
    DataConverter DataConverter,
    ILogger Logger,
    IServiceProvider Services,
    TimerOptions TimerOptions);

/// <summary>
/// Struct representing an invocation context.
/// </summary>
/// <param name="WorkerContext">The worker context.</param>
/// <param name="RuntimeState">The orchestration runtime state.</param>
record struct OrchestrationInvocationContext(
    WorkerContext WorkerContext,
    OrchestrationRuntimeState RuntimeState);