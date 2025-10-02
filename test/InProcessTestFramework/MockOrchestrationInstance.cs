// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// Represents an orchestration instance for in-process testing.
/// Tracks the state, metadata, and execution context of a running orchestration.
/// </summary>
internal class MockOrchestrationInstance
{
    public MockOrchestrationInstance(string instanceId, TaskName name, object? input)
    {
        this.InstanceId = instanceId;
        this.Name = name;
        this.Input = input;
        this.CreatedAt = DateTime.UtcNow;
        this.LastUpdatedAt = DateTime.UtcNow;
        this.RuntimeStatus = OrchestrationRuntimeStatus.Pending;
    }

    public string InstanceId { get; }
    public TaskName Name { get; }
    public object? Input { get; }
    public object? Output { get; set; }
    public OrchestrationRuntimeStatus RuntimeStatus { get; set; }
    public DateTime CreatedAt { get; }
    public DateTime LastUpdatedAt { get; set; }
    public TaskFailureDetails? FailureDetails { get; set; }
    public InProcessTaskOrchestrationContext? Context { get; set; }
}
