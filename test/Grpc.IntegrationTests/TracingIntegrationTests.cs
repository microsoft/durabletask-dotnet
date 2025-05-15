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

    [Fact]
    public async Task Orchestration_Traces()
    {
        var activities = new List<Activity>();

        using var listener = new ActivityListener();

        listener.ShouldListenTo = s => s.Name == nameof(TracingIntegrationTests) || s.Name == "Microsoft.DurableTask";
        listener.Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData;
        listener.ActivityStopped = a => activities.Add(a);

        ActivitySource.AddActivityListener(listener);

        TaskName orchestratorName = nameof(Orchestration_Traces);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, int>(
                    orchestratorName, (ctx, input) => PageableOrchestrationAsync(ctx, input))
                .AddActivityFunc<PageRequest, Page<string>?>(
                    nameof(PageableActivityAsync), (_, input) => PageableActivityAsync(input)));
        });

        using (var testActivity = TestActivitySource.StartActivity("Test", ActivityKind.Client))
        {
            string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
                orchestratorName, input: string.Empty);
            OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
                instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        }

        var testActivity = activities.Single(a => a.OperationName == "Test");

        activities.Should()
            .ContainSingle(a => a.OperationName == "orchestration:Orchestration_Traces")
            .And.Match(a => a.par.Should().Be(testActivity.Id);
        
        activities.Should().HaveCountGreaterThan(0);
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
