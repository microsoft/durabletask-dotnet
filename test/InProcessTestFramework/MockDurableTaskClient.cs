// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// A mock implementation of DurableTaskClient for in-process testing.
/// This client schedules orchestrations to run directly in the same process
/// without requiring a full backend service.
/// </summary>
public sealed class MockDurableTaskClient : DurableTaskClient
{
    readonly InProcessTestOrchestrationService orchestrationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MockDurableTaskClient"/> class.
    /// </summary>
    /// <param name="orchestrationService">The in-process orchestration service.</param>
    public MockDurableTaskClient(InProcessTestOrchestrationService orchestrationService)
        : base("MockDurableTaskClient")
    {
        this.orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
    }

    /// <inheritdoc/>
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName, 
        object? input = null, 
        StartOrchestrationOptions? options = null, 
        CancellationToken cancellation = default)
    {
        var instanceId = options?.InstanceId ?? Guid.NewGuid().ToString("N");
        
        await this.orchestrationService.ScheduleOrchestrationAsync(
            orchestratorName, 
            instanceId, 
            input,
            options,
            cancellation);
            
        return instanceId;
    }

    /// <inheritdoc/>
    public override Task<OrchestrationMetadata> GetInstanceAsync(
        string instanceId, 
        bool getInputsAndOutputs = false, 
        CancellationToken cancellation = default)
    {
        return this.orchestrationService.GetInstanceAsync(instanceId, getInputsAndOutputs, cancellation);
    }

    /// <inheritdoc/>
    public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, 
        bool getInputsAndOutputs = false, 
        CancellationToken cancellation = default)
    {
        return this.orchestrationService.WaitForInstanceStartAsync(instanceId, getInputsAndOutputs, cancellation);
    }

    /// <inheritdoc/>
    public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, 
        bool getInputsAndOutputs = false, 
        CancellationToken cancellation = default)
    {
        return this.orchestrationService.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs, cancellation);
    }

    /// <inheritdoc/>
    public override Task RaiseEventAsync(
        string instanceId, 
        string eventName, 
        object? eventPayload = null, 
        CancellationToken cancellation = default)
    {
        return this.orchestrationService.RaiseEventAsync(instanceId, eventName, eventPayload, cancellation);
    }

    /// <inheritdoc/>
    public override Task SuspendInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        return this.orchestrationService.SuspendInstanceAsync(instanceId, reason, cancellation);
    }

    /// <inheritdoc/>
    public override Task ResumeInstanceAsync(string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        return this.orchestrationService.ResumeInstanceAsync(instanceId, reason, cancellation);
    }

    /// <inheritdoc/>
    public override Task TerminateInstanceAsync(string instanceId, object? output = null, CancellationToken cancellation = default)
    {
        return this.orchestrationService.TerminateInstanceAsync(instanceId, output, cancellation);
    }

    /// <inheritdoc/>
    public override DurableEntityClient Entities => throw new NotSupportedException("Entity support not implemented in mock client");

    /// <inheritdoc/>
    public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
     => throw new NotSupportedException("GetAllInstancesAsync is not supported in the mock client");

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId,
        bool getInputsAndOutputs = false,
        CancellationToken cancellation = default)
        => throw new NotSupportedException("GetInstancesAsync is not supported in the mock client");

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        this.orchestrationService?.Dispose();
        await Task.CompletedTask;
    }
}
