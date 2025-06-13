// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// NOTE: Modified from https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/TraceActivityConstants.cs

namespace Microsoft.DurableTask.Tracing;

class TraceActivityConstants
{
    public const string Orchestration = "orchestration";
    public const string Activity = "activity";
    public const string Event = "event";
    public const string Timer = "timer";

    public const string CreateOrchestration = "create_orchestration";
    public const string OrchestrationEvent = "orchestration_event";
}
