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
    /// Verifies explicit activity version selection does not fall back to an unversioned registration.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedActivity_ExplicitActivityVersionDoesNotFallBackToUnversionedRegistration()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<ExplicitActivityVersionNoFallbackOrchestrationV2>();
                tasks.AddActivity<UnversionedActivityVersionNoFallbackActivity>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "ExplicitActivityVersionNoFallbackOrchestration",
            input: 5,
            new StartOrchestrationOptions
            {
                Version = new TaskVersion("v2"),
            });
        OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.FailureDetails);
        Assert.Equal(typeof(TaskFailedException).FullName, metadata.FailureDetails.ErrorType);
        Assert.NotNull(metadata.FailureDetails.InnerFailure);
        Assert.Equal("ActivityTaskNotFound", metadata.FailureDetails.InnerFailure.ErrorType);
        Assert.Contains(
            "No activity task named 'ExplicitActivityVersionNoFallbackActivity' with version 'v1' was found.",
            metadata.FailureDetails.InnerFailure.ErrorMessage);
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
    /// Verifies user-supplied tags cannot spoof the internal explicit-version marker.
    /// </summary>
    [Fact]
    public async Task ClassBasedVersionedActivity_UserSuppliedReservedTagDoesNotDisableInheritedFallback()
    {
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks =>
            {
                tasks.AddOrchestrator<SpoofedActivityVersionTagFallbackOrchestrationV2>();
                tasks.AddActivity<UnversionedSpoofedActivityVersionTagFallbackActivity>();
            });
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            "SpoofedActivityVersionTagFallbackOrchestration",
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
    /// Verifies the in-proc task-scheduled serializer preserves activity tags.
    /// </summary>
    [Fact]
    public void TaskScheduledEventSerialization_PreservesExplicitVersionMarker()
    {
        TaskScheduledEvent scheduledEvent = new(
            eventId: 7,
            name: "VersionedActivityOverrideActivity",
            version: "v1",
            input: "5")
        {
            Tags = new Dictionary<string, string>
            {
                [ExplicitVersionTagName] = bool.TrueString,
            },
        };

        var proto = ProtobufUtils.ToHistoryEventProto(scheduledEvent);

        Assert.Equal("VersionedActivityOverrideActivity", proto.TaskScheduled.Name);
        Assert.Equal("v1", proto.TaskScheduled.Version);
        Assert.True(
            proto.TaskScheduled.Tags.TryGetValue(ExplicitVersionTagName, out string? tagValue),
            $"Expected tag '{ExplicitVersionTagName}' to be present.");
        Assert.Equal(bool.TrueString, tagValue);
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
