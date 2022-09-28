// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

record WorkerContext(
    DataConverter DataConverter,
    ILogger Logger,
    IServiceProvider Services,
    TimerOptions TimerOptions);

record struct OrchestrationInvocationContext(WorkerContext WorkerContext, OrchestrationRuntimeState RuntimeState);