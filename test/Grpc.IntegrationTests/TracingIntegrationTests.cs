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

    static readonly ActivitySource TestActivitySource = new(TestActivitySourceName);

    [Fact]
    public async Task Orchestration_Traces()
    {
        var activities = new List<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        string orchestratorName = nameof(Orchestration_Traces);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, int>(
                    orchestratorName, (ctx, input) => PageableOrchestrationAsync(ctx, input))
                .AddActivityFunc<PageRequest, Page<string>?>(
                    nameof(PageableActivityAsync), (_, input) => PageableActivityAsync(input)));
        });

        string instanceId;

        using (var activity = TestActivitySource.StartActivity("Test", ActivityKind.Client))
        {
            instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(
                orchestratorName, input: string.Empty);
            OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(
                instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        }

        var testActivity = activities.Single(a => a.Source == TestActivitySource && a.OperationName == "Test");
        var createActivity = activities.Single(a => a.Source.Name == CoreActivitySourceName && a.OperationName == "create_orchestration:Orchestration_Traces");

        // The creation activity should be parented to the test activity.
        createActivity.ParentId.Should().Be(testActivity.Id);
        createActivity.ParentSpanId.Should().Be(testActivity.SpanId);
        createActivity.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
        createActivity.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(orchestratorName);
        createActivity.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");

        var orchestrationActivities = activities.Where(a => a.Source.Name == CoreActivitySourceName && a.OperationName == "orchestration:Orchestration_Traces").ToList();

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
                
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(orchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var activityActivities = activities.Where(a => a.Source.Name == CoreActivitySourceName && a.OperationName == "activity:PageableActivityAsync").ToList();

        activityActivities
            .Should().HaveCountGreaterThan(0)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);
                
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(nameof(PageableActivityAsync));
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("activity");
            });

        activityActivities
            .Zip(Enumerable.Range(0, Int32.MaxValue))
            .Should().AllSatisfy(indexed =>
            {
                indexed.First.TagObjects.Should().ContainKey("durabletask.task.task_id").WhoseValue.Should().Be(indexed.Second);
            });

        var activityExecutionActivities = activities.Where(a => a.Source.Name == TestActivitySourceName && a.OperationName == nameof(PageableActivityAsync)).ToList();

        activityExecutionActivities
            .Should().HaveCount(activityActivities.Count)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().BeOneOf(activityActivities.Select(aa => aa.Id));
                a.ParentSpanId.ToString().Should().BeOneOf(activityActivities.Select(aa => aa.SpanId.ToString()));
            });
    }

    static Task<Page<string>?> PageableActivityAsync(PageRequest? input)
    {
        using var activity = TestActivitySource.StartActivity();

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
