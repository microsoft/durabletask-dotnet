// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using DurableTask.Core.History;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing.Sidecar.Grpc;
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
    /// Verifies explicit activity versions override the inherited orchestration version.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedActivity_ExplicitActivityVersionOverridesOrchestrationVersion()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<VersionedActivityOverrideOrchestrationV2>();
                tasks.AddActivity<VersionedActivityOverrideActivityV1>();
                tasks.AddActivity<VersionedActivityOverrideActivityV2>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "VersionedActivityOverrideOrchestration",
            input: 5,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v2"),
            });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("activity-v1:5", metadata.ReadOutputAs<string>());
    }

    /// <summary>
    /// Verifies inherited orchestration-version activity routing still falls back to an unversioned registration.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedActivity_InheritedVersionFallsBackToUnversionedRegistration()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<InheritedActivityVersionFallbackOrchestrationV2>();
                tasks.AddActivity<UnversionedInheritedActivityVersionFallbackActivity>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "InheritedActivityVersionFallbackOrchestration",
            input: 5,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v2"),
            });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("activity-unversioned:5", metadata.ReadOutputAs<string>());
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

    /// <summary>
    /// Verifies UseVersioning(MatchStrategy = CurrentOrOlder) composes with multi-version registrations:
    /// the per-task registry picks the implementation that exactly matches the inbound instance version,
    /// while UseVersioning's strategy still gates which instance versions the worker accepts. This is
    /// the central composition property of the simplification — the two features are not mutually
    /// exclusive.
    /// </summary>
    [Fact]
    public async Task UseVersioning_CurrentOrOlder_WithMultiVersionRegistry_RoutesEachVersionToItsImplementation()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
            {
                Version = "v2",
                DefaultVersion = "v2",
                MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.CurrentOrOlder,
            });
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<VersionedClassSyntaxV1>();
                tasks.AddOrchestrator<VersionedClassSyntaxV2>();
            });
        });

        // v1 instance is accepted (<= worker v2) and dispatched to V1.
        string v1Id = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "VersionedClassSyntax",
            input: 5,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v1"),
            });
        OrchestrationMetadata v1Metadata = await server.Client.WaitForInstanceCompletionAsync(
            v1Id, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(v1Metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, v1Metadata.RuntimeStatus);
        Assert.Equal("v1:5", v1Metadata.ReadOutputAs<string>());

        // v2 instance is accepted and dispatched to V2.
        string v2Id = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "VersionedClassSyntax",
            input: 5,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v2"),
            });
        OrchestrationMetadata v2Metadata = await server.Client.WaitForInstanceCompletionAsync(
            v2Id, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(v2Metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, v2Metadata.RuntimeStatus);
        Assert.Equal("v2:5", v2Metadata.ReadOutputAs<string>());
    }
}
