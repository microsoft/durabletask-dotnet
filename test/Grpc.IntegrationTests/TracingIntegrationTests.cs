// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using FluentAssertions;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class TracingIntegrationTests : IntegrationTestBase
{
    public TracingIntegrationTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    {
    }

    static readonly ActivitySource TestActivitySource = new ActivitySource(nameof(TracingIntegrationTests));

    static ActivityListener CreateListener(string source, ICollection<Activity> activities)
    {
        ActivityListener listener = new();

        listener.ShouldListenTo = s => s.Name == source;
        listener.Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData;
        listener.ActivityStopped = a => activities.Add(a);

        ActivitySource.AddActivityListener(listener);

        return listener;
    }
    
    [Fact]
    public async Task Orchestration_Traces()
    {
        var testActivities = new List<Activity>();
        var coreActivities = new List<Activity>();

        using var testListener = CreateListener(nameof(TracingIntegrationTests), testActivities);
        using var coreListener = CreateListener("Microsoft.DurableTask", coreActivities);

        TaskName orchestratorName = nameof(Orchestration_Traces);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, int>(
                    orchestratorName, (ctx, input) => PageableOrchestrationAsync(ctx, input))
                .AddActivityFunc<PageRequest, Page<string>?>(
                    nameof(PageableActivityAsync), (_, input) => PageableActivityAsync(input)));
        });

        using (var activity = TestActivitySource.StartActivity("Test", ActivityKind.Client))
        {
            string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
                orchestratorName, input: string.Empty);
            OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
                instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        }

        testActivities.Count.Should().Be(1);

        var testActivity = testActivities.Single();

        coreActivities.Count.Should().Be(1);
        
        var coreActivity = coreActivities.Single();

        coreActivity.ParentId.Should().Be(testActivity.Id);
    }

    static Task<Page<string>?> PageableActivityAsync(PageRequest? input)
    {
        int pageSize = input?.PageSize ?? 3;
        Page<string> CreatePage(string? next)
            => new (Enumerable.Range(0, pageSize).Select(x => $"item_{x}").ToList(), next);
        Page<string>? page = input?.Continuation switch
        {
            null => CreatePage("1"),
            "1" => CreatePage("2"),
            "2" => CreatePage(null),
            _ => null,
        };

        return Task.FromResult(page);
    }

    static async Task<int> PageableOrchestrationAsync(TaskOrchestrationContext context, string? input)
    {
        AsyncPageable<string> pageable = Pageable.Create((continuation, _, _) =>
        {
            return context.CallActivityAsync<Page<string>?>(
                nameof(PageableActivityAsync), new PageRequest(continuation))!;
        });

        return await pageable.CountAsync();
    }

    record PageRequest(string? Continuation, int? PageSize = null);
}
