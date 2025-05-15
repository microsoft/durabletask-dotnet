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

    static ActivityListener CreateListener(string[] sources, ICollection<Activity> activities)
    {
        ActivityListener listener = new();

        listener.ShouldListenTo = s => sources.Contains(s.Name);
        listener.Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData;
        listener.ActivityStopped = a => activities.Add(a);

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    const string TestActivitySourceName = nameof(TracingIntegrationTests);
    const string CoreActivitySourceName = "Microsoft.DurableTask";

    static readonly string[] ActivitySourceNames = [TestActivitySourceName, CoreActivitySourceName];

    [Fact]
    public async Task Orchestration_Traces()
    {
        ActivitySource testActivitySource = new ActivitySource(nameof(TracingIntegrationTests));

        var activities = new List<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        TaskName orchestratorName = nameof(Orchestration_Traces);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, int>(
                    orchestratorName, (ctx, input) => PageableOrchestrationAsync(ctx, input))
                .AddActivityFunc<PageRequest, Page<string>?>(
                    nameof(PageableActivityAsync), (_, input) => PageableActivityAsync(input)));
        });

        using (var activity = testActivitySource.StartActivity("Test", ActivityKind.Client))
        {
            string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
                orchestratorName, input: string.Empty);
            OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
                instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        }

        var testActivity = activities.Single(a => a.Source == testActivitySource && a.OperationName == "Test");
        var createActivity = activities.Single(a => a.Source.Name == CoreActivitySourceName && a.OperationName == "create_orchestration:Orchestration_Traces");

        // The creation activity should be parented to the test activity.
        createActivity.ParentId.Should().Be(testActivity.Id);
        createActivity.ParentSpanId.Should().Be(testActivity.SpanId);

        var orchestrationActivities = activities.Where(a => a.Source.Name == CoreActivitySourceName && a.OperationName == "orchestration:Orchestration_Traces");

        // The orchestration activities should be the same "logical" activity.
        orchestrationActivities.Select(a => a.StartTimeUtc).Distinct().Should().HaveCount(1);
        orchestrationActivities.Select(a => a.Id).Distinct().Should().HaveCount(1);
        orchestrationActivities.Select(a => a.SpanId).Distinct().Should().HaveCount(1);

        // The orchestration activities should be parented to the create activity.
        orchestrationActivities
            .Should().HaveCountGreaterThan(0)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(createActivity.Id);
                a.ParentSpanId.Should().Be(createActivity.SpanId);
            });
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
