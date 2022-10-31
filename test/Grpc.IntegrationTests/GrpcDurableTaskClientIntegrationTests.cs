// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class DurableTaskGrpcClientIntegrationTests : IntegrationTestBase
{
    const string OrchestrationName = "TestOrchestration";

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
        OrchestrationMetadata? metadata = await server.Client.GetInstanceMetadataAsync(instanceId, false);
        AssertMetadata(metadata!, instanceId, OrchestrationRuntimeStatus.Running);

        await server.Client.RaiseEventAsync(instanceId, "event", default);
        await server.Client.WaitForInstanceCompletionAsync(instanceId, default);
        metadata = await server.Client.GetInstanceMetadataAsync(instanceId, false);
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
            x => server.Client.ScheduleNewOrchestrationInstanceAsync(OrchestrationName, x, input: false));
        AsyncPageable<OrchestrationMetadata> pageable = server.Client.GetInstances(query);

        await ForEachOrchestrationAsync(x => server.Client.WaitForInstanceStartAsync(x, default));
        List<OrchestrationMetadata> metadata = await pageable.ToListAsync();
        metadata.Should().HaveCount(2)
            .And.AllSatisfy(m => AssertMetadata(m, OrchestrationRuntimeStatus.Running))
            .And.NotContain(x => string.Equals(x.InstanceId, notIncluded, StringComparison.OrdinalIgnoreCase));

        await ForEachOrchestrationAsync(x => server.Client.RaiseEventAsync(x, "event", default));
        await ForEachOrchestrationAsync(x => server.Client.WaitForInstanceCompletionAsync(x, default));
        metadata = await pageable.ToListAsync();
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
                OrchestrationName, $"GetInstances_AsPages_EndToEnd-{i}", input: false);
        }

        AsyncPageable<OrchestrationMetadata> pageable = server.Client.GetInstances(query);
        List<Page<OrchestrationMetadata>> pages = await pageable.AsPages(pageSizeHint: 5).ToListAsync();
        pages.Should().HaveCount(5);
        pages.ForEach(p => p.Values.Should().HaveCount(p.ContinuationToken is null ? 1 : 5));

        List<Page<OrchestrationMetadata>> resumedPages = await pageable.AsPages(
            pages[1].ContinuationToken, pageSizeHint: 4).ToListAsync();
        resumedPages.Should().HaveCount(3);

        List<OrchestrationMetadata> left = resumedPages.SelectMany(p => p.Values).ToList();
        List<OrchestrationMetadata> right = pages.Skip(2).SelectMany(p => p.Values).ToList();
        left.Should().BeEquivalentTo(right, cfg => cfg.Including(x => x.InstanceId).Including(x => x.CreatedAt));

        Page<OrchestrationMetadata> page = await pageable.AsPages(pageSizeHint: 10).FirstAsync();
        page.Values.Should().HaveCount(10);
        page.ContinuationToken.Should().NotBeNull();
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
            b.AddTasks(tasks => tasks.AddOrchestrator<bool, string>(OrchestrationName, Orchestration));
        });
    }
}
