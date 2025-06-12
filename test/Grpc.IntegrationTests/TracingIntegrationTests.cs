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
    public async Task MultiTaskOrchestration()
    {
        var activities = new List<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        string orchestratorName = nameof(MultiTaskOrchestration);
        string activityName = nameof(TestActivityAsync);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    async (ctx, input) =>
                    {
                        await ctx.CallActivityAsync(nameof(TestActivityAsync), true);
                        await ctx.CallActivityAsync(nameof(TestActivityAsync), true);

                        return true;
                    })
                .AddActivityFunc<bool, bool>(activityName, (_, input) => TestActivityAsync(input)));
        });

        string instanceId;

        using (var activity = TestActivitySource.StartActivity("Test"))
        {
            instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: true, cancellation: this.TimeoutToken);

            OrchestrationMetadata metadata = await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, this.TimeoutToken);
        }

        var testActivity = activities.Single(a => a.Source == TestActivitySource && a.OperationName == "Test");
        var createActivity = activities.Single(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"create_orchestration:{orchestratorName}");

        // The creation activity should be parented to the test activity.
        createActivity.ParentId.Should().Be(testActivity.Id);
        createActivity.ParentSpanId.Should().Be(testActivity.SpanId);
        createActivity.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
        createActivity.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(orchestratorName);
        createActivity.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");

        var orchestrationActivities = activities.Where(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration:{orchestratorName}").ToList();

        orchestrationActivities.Should().HaveCountGreaterThan(0);

        // The orchestration activities should be the same "logical" orchestration activity.
        orchestrationActivities.Select(a => a.StartTimeUtc).Distinct().Should().HaveCount(1);
        orchestrationActivities.Select(a => a.Id).Distinct().Should().HaveCount(1);
        orchestrationActivities.Select(a => a.SpanId).Distinct().Should().HaveCount(1);

        // The orchestration activities should be parented to the create activity.
        orchestrationActivities
            .Should().AllSatisfy(a =>
            {
                a.Kind.Should().Be(ActivityKind.Server);
                a.ParentId.Should().Be(createActivity.Id);
                a.ParentSpanId.Should().Be(createActivity.SpanId);
                
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(orchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var clientActivityActivities = activities.Where(a => a.Kind == ActivityKind.Client && a.Source.Name == CoreActivitySourceName && a.OperationName == $"activity:{activityName}").ToList();

        // The "client" (i.e. scheduled) task activities should be parented to the orchestration activity.
        clientActivityActivities
            .Should().HaveCount(2)
            .And.AllSatisfy(a =>
            {
                a.Kind.Should().Be(ActivityKind.Client);
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);
                
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(activityName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("activity");
                a.TagObjects.Should().ContainKey("durabletask.task.task_id").WhoseValue.Should().NotBeNull();
            });

        clientActivityActivities
            .Zip(Enumerable.Range(0, Int32.MaxValue))
            .Should().AllSatisfy(indexed =>
            {
                indexed.First.TagObjects.Should().ContainKey("durabletask.task.task_id").WhoseValue.Should().Be(indexed.Second);
            });

        var serverActivityActivities = activities.Where(a => a.Kind == ActivityKind.Server && a.Source.Name == CoreActivitySourceName && a.OperationName == $"activity:{activityName}").ToList();

        // The "server" (i.e. executed) task activities should be parented to the client activity activities.
        serverActivityActivities
            .Should().HaveCount(clientActivityActivities.Count)
            .And.AllSatisfy(a =>
            {
                a.Kind.Should().Be(ActivityKind.Server);
                a.ParentId.Should().BeOneOf(clientActivityActivities.Select(aa => aa.Id));
                a.ParentSpanId.ToString().Should().BeOneOf(clientActivityActivities.Select(aa => aa.SpanId.ToString()));

                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(activityName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("activity");
                a.TagObjects.Should().ContainKey("durabletask.task.task_id").WhoseValue.Should().NotBeNull();
            });

        var activityExecutionActivities = activities.Where(a => a.Source.Name == TestActivitySourceName && a.OperationName == nameof(TestActivityAsync)).ToList();

        // The activity execution activities should be parented to the server activity activities.
        activityExecutionActivities
            .Should().HaveCount(serverActivityActivities.Count)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().BeOneOf(serverActivityActivities.Select(aa => aa.Id));
                a.ParentSpanId.ToString().Should().BeOneOf(serverActivityActivities.Select(aa => aa.SpanId.ToString()));
            });
    }

    static Task<bool> TestActivityAsync(bool shouldSucceed)
    {
        using var activity = TestActivitySource.StartActivity();

        if (shouldSucceed)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.FromResult(true);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error);

            return Task.FromResult(false);
        }
    }
}
