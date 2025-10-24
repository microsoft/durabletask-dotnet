// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;
using static Microsoft.DurableTask.Grpc.Tests.ClassSyntaxTestOrchestration;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests for class-based syntax orchestrators and activities.
/// These tests demonstrate how to use TaskOrchestrator and TaskActivity base classes
/// instead of function-based syntax.
/// </summary>
public class ClassSyntaxIntegrationTests : IntegrationTestBase
{
    public ClassSyntaxIntegrationTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    { }

    /// <summary>
    /// Tests a basic orchestration that calls activities in sequence.
    /// </summary>
    [Fact]
    public async Task HelloSequenceClassBased()
    {
        // Register orchestrator and activity
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<HelloSequenceOrchestrator>();
                tasks.AddActivity<SayHelloActivity>();
            });
        });

        // Schedule and wait for the orchestration to complete
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("HelloSequence");
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        // Verify the orchestration completed successfully
        Assert.NotNull(metadata);
        Assert.Equal(instanceId, metadata.InstanceId);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        // Verify the output
        Assert.NotNull(metadata.SerializedOutput);
        List<string>? result = metadata.ReadOutputAs<List<string>>();
        Assert.NotNull(result);
        Assert.Equal("Hello Tokyo!", result[0]);
        Assert.Equal("Hello London!", result[1]);
        Assert.Equal("Hello Seattle!", result[2]);
    }

    /// <summary>
    /// Tests exception handling.
    /// </summary>
    [Fact]
    public async Task ClassBasedOrchestratorException()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<FaultyOrchestrator>();
                tasks.AddActivity<ThrowingActivity>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("FaultyOrchestrator");
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.FailureDetails);
        Assert.Contains("Intentional failure", metadata.FailureDetails.ErrorMessage);
    }

    /// <summary>
    /// Tests activity retry using RetryPolicy.
    /// </summary>
    [Fact]
    public async Task ClassBasedActivityWithRetryPolicy()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<RetryOrchestrator>();
                tasks.AddActivity<RetryableActivity>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("RetryOrchestrator");
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.SerializedOutput);
        string? result = metadata.ReadOutputAs<string>();
        Assert.Equal("Success after retries", result);
    }

    /// <summary>
    /// Tests sub-orchestration.
    /// </summary>
    [Fact]
    public async Task ClassBasedSubOrchestration()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<ParentOrchestrator>();
                tasks.AddOrchestrator<ChildOrchestrator>();
                tasks.AddActivity<ChildActivity>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("ParentOrchestrator", input: 5);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.SerializedOutput);
        int result = metadata.ReadOutputAs<int>();
        Assert.Equal(15, result); // 5 + 10 (from child)
    }

    /// <summary>
    /// Tests external event handling.
    /// </summary>
    [Fact]
    public async Task ClassBasedExternalEvent()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestrator<ExternalEventOrchestrator>());
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync("ExternalEventOrchestrator");
        
        // Wait for the orchestration to start
        await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        // Send an external event
        await server.Client.RaiseEventAsync(instanceId, "ApprovalEvent", "Approved");

        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.SerializedOutput);
        string? result = metadata.ReadOutputAs<string>();
        Assert.Equal("Received: Approved", result);
    }
}
