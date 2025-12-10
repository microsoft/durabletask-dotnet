// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class DurableTaskGrpcClientIntegrationTests : IntegrationTestBase
{
    const string OrchestrationName = "TestOrchestration";
    const int PollingTimeoutSeconds = 5;
    const int PollingIntervalMilliseconds = 100;

    public DurableTaskGrpcClientIntegrationTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    {
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstanceMetadata_EndToEnd(bool shouldThrow)
    {
        static void AssertMetadata(OrchestrationMetadata metadata, string instanceId, OrchestrationRuntimeStatus status)
        {
            metadata.Should().NotBeNull();
            using (new AssertionScope())
            {
                metadata.Name.Should().Be(OrchestrationName);
                metadata.InstanceId.Should().Be(instanceId);
                metadata.SerializedInput.Should().BeNull();
                metadata.SerializedOutput.Should().BeNull();
                metadata.RuntimeStatus.Should().Be(status);
                metadata.FailureDetails.Should().BeNull();
            }
        }

        await using HostTestLifetime server = await this.StartAsync();

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: shouldThrow);

        await server.Client.WaitForInstanceStartAsync(instanceId, default);
        OrchestrationMetadata? metadata = await server.Client.GetInstanceAsync(instanceId, false);
        AssertMetadata(metadata!, instanceId, OrchestrationRuntimeStatus.Running);

        await server.Client.RaiseEventAsync(instanceId, "event", default);
        await server.Client.WaitForInstanceCompletionAsync(instanceId, default);
        metadata = await server.Client.GetInstanceAsync(instanceId, false);
        AssertMetadata(
            metadata!,
            instanceId,
            shouldThrow ? OrchestrationRuntimeStatus.Failed : OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task GetInstances_EndToEnd()
    {
        static void AssertMetadata(OrchestrationMetadata metadata, OrchestrationRuntimeStatus status)
        {
            metadata.Should().NotBeNull();
            using (new AssertionScope())
            {
                metadata.Name.Should().Be(OrchestrationName);
                metadata.InstanceId.Should().StartWith("GetInstances_EndToEnd-");
                metadata.RuntimeStatus.Should().Be(status);
                metadata.FailureDetails.Should().BeNull();

                // InMemoryOrchestrationService always returns these in a query.
                // The NotBeNull() here is to force this test failure when correct behavior
                // is added, so we remember to change bellow to "BeNull()".
                metadata.SerializedInput.Should().NotBeNull();
                if (status == OrchestrationRuntimeStatus.Completed)
                {
                    metadata.SerializedOutput.Should().NotBeNull();
                }
                else
                {
                    metadata.SerializedOutput.Should().BeNull();
                }
            }
        }

        OrchestrationQuery query = new() { InstanceIdPrefix = "GetInstances_EndToEnd" };

        static async Task ForEachOrchestrationAsync(Func<string, Task> func)
        {
            await func("GetInstances_EndToEnd-1");
            await func("GetInstances_EndToEnd-2");
        }

        await using HostTestLifetime server = await this.StartAsync();

        // Enqueue an extra orchestration which we will verify is NOT present.
        string notIncluded = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        await ForEachOrchestrationAsync(
            x => server.Client.ScheduleNewOrchestrationInstanceAsync(
                OrchestrationName, input: false, new StartOrchestrationOptions(x)));
        AsyncPageable<OrchestrationMetadata> pageable = server.Client.GetAllInstancesAsync(query);

        await ForEachOrchestrationAsync(x => server.Client.WaitForInstanceStartAsync(x, default));
        List<OrchestrationMetadata> metadata = await pageable.ToListAsync(CancellationToken.None);
        metadata.Should().HaveCount(2)
            .And.AllSatisfy(m => AssertMetadata(m, OrchestrationRuntimeStatus.Running))
            .And.NotContain(x => string.Equals(x.InstanceId, notIncluded, StringComparison.OrdinalIgnoreCase));

        await ForEachOrchestrationAsync(x => server.Client.RaiseEventAsync(x, "event", default));
        await ForEachOrchestrationAsync(x => server.Client.WaitForInstanceCompletionAsync(x, default));
        metadata = await pageable.ToListAsync(CancellationToken.None);
        metadata.Should().HaveCount(2)
            .And.AllSatisfy(m => AssertMetadata(m, OrchestrationRuntimeStatus.Completed))
            .And.NotContain(x => string.Equals(x.InstanceId, notIncluded, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInstances_AsPages_EndToEnd()
    {
        OrchestrationQuery query = new() { InstanceIdPrefix = "GetInstances_AsPages_EndToEnd" };
        await using HostTestLifetime server = await this.StartAsync();

        for (int i = 0; i < 21; i++)
        {
            await server.Client.ScheduleNewOrchestrationInstanceAsync(
                OrchestrationName, input: false, new StartOrchestrationOptions($"GetInstances_AsPages_EndToEnd-{i}"));
        }

        AsyncPageable<OrchestrationMetadata> pageable = server.Client.GetAllInstancesAsync(query);
        List<Page<OrchestrationMetadata>> pages = await pageable.AsPages(pageSizeHint: 5).ToListAsync(CancellationToken.None);
        pages.Should().HaveCount(5);
        pages.ForEach(p => p.Values.Should().HaveCount(p.ContinuationToken is null ? 1 : 5));

        List<Page<OrchestrationMetadata>> resumedPages = await pageable.AsPages(
            pages[1].ContinuationToken, pageSizeHint: 4).ToListAsync(CancellationToken.None);
        resumedPages.Should().HaveCount(3);

        List<OrchestrationMetadata> left = resumedPages.SelectMany(p => p.Values).ToList();
        List<OrchestrationMetadata> right = pages.Skip(2).SelectMany(p => p.Values).ToList();
        left.Should().BeEquivalentTo(
            right,
            cfg => cfg.Including(x => x.InstanceId).Including(x => x.CreatedAt)
                .Using<DateTimeOffset, DateTimeToleranceComparer>());

        Page<OrchestrationMetadata> page = await pageable.AsPages(pageSizeHint: 10).FirstAsync(CancellationToken.None);
        page.Values.Should().HaveCount(10);
        page.ContinuationToken.Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeInstance_EndToEnd()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartAsync();

        // Create and complete an orchestration instance
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        // Wait for it to start and raise event to complete it
        await server.Client.WaitForInstanceStartAsync(instanceId, default);
        await server.Client.RaiseEventAsync(instanceId, "event", default);
        await server.Client.WaitForInstanceCompletionAsync(instanceId, default);

        // Verify instance exists before purge
        OrchestrationMetadata? metadata = await server.Client.GetInstanceAsync(instanceId, false);
        metadata.Should().NotBeNull();
        metadata!.InstanceId.Should().Be(instanceId);
        metadata.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

        // Act
        PurgeResult result = await server.Client.PurgeInstanceAsync(
            instanceId,
            new PurgeInstanceOptions { Recursive = true });

        // Assert
        result.Should().NotBeNull();
        result.PurgedInstanceCount.Should().Be(1);
        result.IsComplete.Should().NotBeFalse();
        // Verify instance no longer exists
        OrchestrationMetadata? instance = await server.Client.GetInstanceAsync(instanceId, false);
        instance.Should().BeNull();
    }

    [Fact]
    public async Task PurgeInstances_WithFilter_EndToEnd()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartAsync();
        List<string> instanceIds = new List<string>();

        // Create multiple instances
        for (int i = 0; i < 3; i++)
        {
            string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
                OrchestrationName, input: false);
            instanceIds.Add(instanceId);

            // Wait for it to start and raise event to complete it
            await server.Client.WaitForInstanceStartAsync(instanceId, default);
            await server.Client.RaiseEventAsync(instanceId, "event", default);
            await server.Client.WaitForInstanceCompletionAsync(instanceId, default);
        }

        // Act
        PurgeResult result = await server.Client.PurgeAllInstancesAsync(
            new PurgeInstancesFilter(
                CreatedFrom: DateTime.UtcNow.AddMinutes(-5),
                CreatedTo: DateTime.UtcNow,
                Statuses: new[] { OrchestrationRuntimeStatus.Completed }));

        // Assert
        result.Should().NotBeNull();
        result.PurgedInstanceCount.Should().BeGreaterThan(3);
        result.IsComplete.Should().NotBeFalse();
        // Verify instances no longer exist
        foreach (string instanceId in instanceIds)
        {
            OrchestrationMetadata? instance = await server.Client.GetInstanceAsync(instanceId, false);
            instance.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartAsync_EndToEnd(bool restartWithNewInstanceId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await using HostTestLifetime server = await this.StartAsync();

        // Start an initial orchestration with shouldThrow = false to ensure it completes successfully
        string originalInstanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        // Wait for it to start and then complete
        await server.Client.WaitForInstanceStartAsync(originalInstanceId, default);
        await server.Client.RaiseEventAsync(originalInstanceId, "event", default);
        await server.Client.WaitForInstanceCompletionAsync(originalInstanceId, cts.Token);

        // Verify the original orchestration completed
        OrchestrationMetadata? originalMetadata = await server.Client.GetInstanceAsync(originalInstanceId, true);
        originalMetadata.Should().NotBeNull();
        originalMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

        // Restart the orchestration
        string restartedInstanceId = await server.Client.RestartAsync(originalInstanceId, restartWithNewInstanceId);

        // Verify the restart behavior
        if (restartWithNewInstanceId)
        {
            restartedInstanceId.Should().NotBe(originalInstanceId);
        }
        else
        {
            restartedInstanceId.Should().Be(originalInstanceId);
        }

        // Complete the restarted orchestration
        await server.Client.RaiseEventAsync(restartedInstanceId, "event");

        using var completionCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await server.Client.WaitForInstanceCompletionAsync(restartedInstanceId, completionCts.Token);

        // Verify the restarted orchestration completed.
        // Also verify input and orchestrator name are matched.
        var restartedMetadata = await server.Client.GetInstanceAsync(restartedInstanceId, true);
        restartedMetadata.Should().NotBeNull();
        restartedMetadata!.Name.Should().Be(OrchestrationName);
        restartedMetadata.SerializedInput.Should().Be("false");
        restartedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task RestartAsync_InstanceNotFound_ThrowsArgumentException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)); // 1-minute timeout
        await using HostTestLifetime server = await this.StartAsync();

        // Try to restart a non-existent orchestration
        Func<Task> restartAction = () => server.Client.RestartAsync("non-existent-instance-id", cancellation: cts.Token);

        // Should throw ArgumentException
        await restartAction.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*An orchestration with the instanceId non-existent-instance-id was not found*");
    }

    [Fact]
    public async Task SuspendAndResumeInstance_EndToEnd()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartLongRunningAsync();

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        // Wait for the orchestration to start
        await server.Client.WaitForInstanceStartAsync(instanceId, default);

        // Act - Suspend the orchestration
        await server.Client.SuspendInstanceAsync(instanceId, "Test suspension", default);

        // Poll for suspended status
        OrchestrationMetadata? suspendedMetadata = await this.PollForStatusAsync(
            server.Client, instanceId, OrchestrationRuntimeStatus.Suspended, default);

        // Assert - Verify orchestration is suspended
        suspendedMetadata.Should().NotBeNull();
        suspendedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Suspended);
        suspendedMetadata.InstanceId.Should().Be(instanceId);

        // Act - Resume the orchestration
        await server.Client.ResumeInstanceAsync(instanceId, "Test resumption", default);

        // Poll for running status
        OrchestrationMetadata? resumedMetadata = await this.PollForStatusAsync(
            server.Client, instanceId, OrchestrationRuntimeStatus.Running, default);

        // Assert - Verify orchestration is running again
        resumedMetadata.Should().NotBeNull();
        resumedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Running);

        // Complete the orchestration
        await server.Client.RaiseEventAsync(instanceId, "event", default);
        await server.Client.WaitForInstanceCompletionAsync(instanceId, default);

        // Verify the orchestration completed successfully
        OrchestrationMetadata? completedMetadata = await server.Client.GetInstanceAsync(instanceId, false);
        completedMetadata.Should().NotBeNull();
        completedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task SuspendInstance_WithoutReason_Succeeds()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartLongRunningAsync();

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        await server.Client.WaitForInstanceStartAsync(instanceId, default);

        // Act - Suspend without a reason
        await server.Client.SuspendInstanceAsync(instanceId, cancellation: default);

        // Poll for suspended status
        OrchestrationMetadata? suspendedMetadata = await this.PollForStatusAsync(
            server.Client, instanceId, OrchestrationRuntimeStatus.Suspended, default);

        // Assert
        suspendedMetadata.Should().NotBeNull();
        suspendedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Suspended);
    }

    [Fact]
    public async Task ResumeInstance_WithoutReason_Succeeds()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartLongRunningAsync();

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        await server.Client.WaitForInstanceStartAsync(instanceId, default);
        await server.Client.SuspendInstanceAsync(instanceId, "Test suspension", default);

        // Wait for suspension
        await this.PollForStatusAsync(server.Client, instanceId, OrchestrationRuntimeStatus.Suspended, default);

        // Act - Resume without a reason
        await server.Client.ResumeInstanceAsync(instanceId, cancellation: default);

        // Poll for running status
        OrchestrationMetadata? resumedMetadata = await this.PollForStatusAsync(
            server.Client, instanceId, OrchestrationRuntimeStatus.Running, default);

        // Assert
        resumedMetadata.Should().NotBeNull();
        resumedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Running);
    }

    [Fact]
    public async Task SuspendInstance_AlreadyCompleted_NoError()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartAsync();

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        await server.Client.WaitForInstanceStartAsync(instanceId, default);
        await server.Client.RaiseEventAsync(instanceId, "event", default);
        await server.Client.WaitForInstanceCompletionAsync(instanceId, default);

        // Verify it's completed
        OrchestrationMetadata? completedMetadata = await server.Client.GetInstanceAsync(instanceId, false);
        completedMetadata.Should().NotBeNull();
        completedMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);

        // Act - Try to suspend a completed orchestration (should not throw)
        await server.Client.SuspendInstanceAsync(instanceId, "Test suspension", default);

        // Assert - Status should remain completed
        OrchestrationMetadata? metadata = await server.Client.GetInstanceAsync(instanceId, false);
        metadata.Should().NotBeNull();
        metadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
    }

    [Fact]
    public async Task ResumeInstance_NotSuspended_NoError()
    {
        // Arrange
        await using HostTestLifetime server = await this.StartLongRunningAsync();

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
            OrchestrationName, input: false);

        await server.Client.WaitForInstanceStartAsync(instanceId, default);

        // Verify it's running
        OrchestrationMetadata? runningMetadata = await server.Client.GetInstanceAsync(instanceId, false);
        runningMetadata.Should().NotBeNull();
        runningMetadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Running);

        // Act - Try to resume an already running orchestration (should not throw)
        await server.Client.ResumeInstanceAsync(instanceId, "Test resumption", default);

        // Assert - Status should remain running
        OrchestrationMetadata? metadata = await server.Client.GetInstanceAsync(instanceId, false);
        metadata.Should().NotBeNull();
        metadata!.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Running);
    }

    Task<HostTestLifetime> StartAsync()
    {
        static async Task<string> Orchestration(TaskOrchestrationContext context, bool shouldThrow)
        {
            context.SetCustomStatus("waiting");
            await context.WaitForExternalEvent<string>("event");
            if (shouldThrow)
            {
                throw new InvalidOperationException("Orchestration failed");
            }

            return $"{shouldThrow} -> output";
        }

        return this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc<bool, string>(OrchestrationName, Orchestration));
        });
    }

    Task<HostTestLifetime> StartLongRunningAsync()
    {
        static async Task<string> LongRunningOrchestration(TaskOrchestrationContext context, bool shouldThrow)
        {
            context.SetCustomStatus("waiting");
            // Wait for external event or a timer (30 seconds) to allow suspend/resume operations
            Task<string> eventTask = context.WaitForExternalEvent<string>("event");
            Task timerTask = context.CreateTimer(TimeSpan.FromSeconds(30), CancellationToken.None);
            await Task.WhenAny(eventTask, timerTask);
            
            if (shouldThrow)
            {
                throw new InvalidOperationException("Orchestration failed");
            }

            return $"{shouldThrow} -> output";
        }

        return this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc<bool, string>(OrchestrationName, LongRunningOrchestration));
        });
    }

    async Task<OrchestrationMetadata?> PollForStatusAsync(
        DurableTaskClient client,
        string instanceId,
        OrchestrationRuntimeStatus expectedStatus,
        CancellationToken cancellation = default)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(PollingTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            OrchestrationMetadata? metadata = await client.GetInstanceAsync(instanceId, false, cancellation);
            if (metadata?.RuntimeStatus == expectedStatus)
            {
                return metadata;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(PollingIntervalMilliseconds), cancellation);
        }

        return await client.GetInstanceAsync(instanceId, false, cancellation);
    }

    class DateTimeToleranceComparer : IEqualityComparer<DateTimeOffset>
    {
        public bool Equals(DateTimeOffset x, DateTimeOffset y) => (x - y).Duration() < TimeSpan.FromMilliseconds(100);

        public int GetHashCode([DisallowNull] DateTimeOffset obj) => obj.GetHashCode();
    }
}
