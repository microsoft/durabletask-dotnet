// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;
using static Microsoft.DurableTask.Grpc.Tests.VersionedClassSyntaxTestOrchestration;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Integration tests for class-based versioned orchestrators.
/// </summary>
public class VersionedClassSyntaxIntegrationTests : IntegrationTestBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VersionedClassSyntaxIntegrationTests"/> class.
    /// </summary>
    public VersionedClassSyntaxIntegrationTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    { }

    /// <summary>
    /// Verifies explicit orchestration versions route to the matching class-based orchestrator.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedOrchestrator_ExplicitVersionRoutesMatchingClass()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<VersionedClassSyntaxV1>();
                tasks.AddOrchestrator<VersionedClassSyntaxV2>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "VersionedClassSyntax",
            input: 5,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v2"),
            });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("v2:5", metadata.ReadOutputAs<string>());
    }

    /// <summary>
    /// Verifies starting without a version fails when only versioned handlers are registered.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedOrchestrator_WithoutVersionFailsWhenOnlyVersionedHandlersExist()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<VersionedClassSyntaxV1>();
                tasks.AddOrchestrator<VersionedClassSyntaxV2>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "VersionedClassSyntax",
            input: 5,
            this.TimeoutToken);
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.FailureDetails);
        Assert.Equal("OrchestratorTaskNotFound", metadata.FailureDetails.ErrorType);
        Assert.Contains("No orchestrator task named 'VersionedClassSyntax' was found.", metadata.FailureDetails.ErrorMessage);
    }

    /// <summary>
    /// Verifies continue-as-new can migrate a class-based orchestration to a newer version.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedOrchestrator_ContinueAsNewNewVersionRoutesToNewClass()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<VersionedContinueAsNewClassSyntaxV1>();
                tasks.AddOrchestrator<VersionedContinueAsNewClassSyntaxV2>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "VersionedContinueAsNewClassSyntax",
            input: 4,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v1"),
            });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("v2:5", metadata.ReadOutputAs<string>());
    }
}
