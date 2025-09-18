// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Concurrent;
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

    static ActivityListener CreateListener(string[] sources, ConcurrentBag<Activity> activities)
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
        var activities = new ConcurrentBag<Activity>();

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

        await server.DisposeAsync();
        listener.Dispose();

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
                a.TagObjects.Should().ContainKey("durabletask.task.status").WhoseValue.Should().Be("Completed");
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var clientActivityActivities = activities.Where(a => a.Kind == ActivityKind.Client && a.Source.Name == CoreActivitySourceName && a.OperationName == $"activity:{activityName}").ToList();

        // The "client" (i.e. scheduled) task activities should be parented to the orchestration activity.
        clientActivityActivities
            .Should().HaveCount(2)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);
                
            });

        var serverActivityActivities = activities.Where(a => a.Kind == ActivityKind.Server && a.Source.Name == CoreActivitySourceName && a.OperationName == $"activity:{activityName}").ToList();

        // The "server" (i.e. executed) task activities should be parented to the client activity activities.
        serverActivityActivities
            .Should().HaveCount(clientActivityActivities.Count)
            .And.AllSatisfy(a =>
            {
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

    [Fact]
    public async Task TaskOrchestrationWithActivityFailure()
    {
        var activities = new ConcurrentBag<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        string orchestratorName = nameof(TaskOrchestrationWithActivityFailure);
        string activityName = nameof(TestActivityAsync);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    async (ctx, input) =>
                    {
                        await ctx.CallActivityAsync(nameof(TestActivityAsync), false);

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

        await server.DisposeAsync();
        listener.Dispose();

        var testActivity = activities.Single(a => a.Source == TestActivitySource && a.OperationName == "Test");
        var createActivity = activities.Single(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"create_orchestration:{orchestratorName}");

        // The creation activity should be parented to the test activity.
        createActivity.ParentId.Should().Be(testActivity.Id);
        createActivity.ParentSpanId.Should().Be(testActivity.SpanId);

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
                a.Status.Should().Be(ActivityStatusCode.Error);
                
                a.TagObjects.Should().ContainKey("durabletask.task.status").WhoseValue.Should().Be("Failed");
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var clientActivityActivities = activities.Where(a => a.Kind == ActivityKind.Client && a.Source.Name == CoreActivitySourceName && a.OperationName == $"activity:{activityName}").ToList();

        // The "client" (i.e. scheduled) task activities should be parented to the orchestration activity.
        clientActivityActivities
            .Should().HaveCount(1)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);
                a.Status.Should().Be(ActivityStatusCode.Error);
            });

        var serverActivityActivities = activities.Where(a => a.Kind == ActivityKind.Server && a.Source.Name == CoreActivitySourceName && a.OperationName == $"activity:{activityName}").ToList();

        // The "server" (i.e. executed) task activities should be parented to the client activity activities.
        serverActivityActivities
            .Should().HaveCount(clientActivityActivities.Count)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().BeOneOf(clientActivityActivities.Select(aa => aa.Id));
                a.ParentSpanId.ToString().Should().BeOneOf(clientActivityActivities.Select(aa => aa.SpanId.ToString()));
                a.Status.Should().Be(ActivityStatusCode.Error);
            });

        var activityExecutionActivities = activities.Where(a => a.Source.Name == TestActivitySourceName && a.OperationName == nameof(TestActivityAsync)).ToList();

        // The activity execution activities should be parented to the server activity activities.
        activityExecutionActivities
            .Should().HaveCount(serverActivityActivities.Count)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().BeOneOf(serverActivityActivities.Select(aa => aa.Id));
                a.ParentSpanId.ToString().Should().BeOneOf(serverActivityActivities.Select(aa => aa.SpanId.ToString()));
                a.Status.Should().Be(ActivityStatusCode.Error);
            });
    }

    [Fact]
    public async Task TaskWithSuborchestration()
    {
        var activities = new ConcurrentBag<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);
        
        string orchestratorName = nameof(TaskWithSuborchestration);
        string subOrchestratorName = "SubOrchestration";
        string activityName = nameof(TestActivityAsync);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    async (ctx, input) =>
                    {
                        await ctx.CallSubOrchestratorAsync(subOrchestratorName, input: true);

                        return true;
                    })
                .AddOrchestratorFunc<bool, bool>(
                    subOrchestratorName,
                    async (ctx, input) =>
                    {
                        await ctx.CallActivityAsync(nameof(TestActivityAsync), input);

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

        await server.DisposeAsync();
        listener.Dispose();

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

        var clientSuborchestrationActivities = activities.Where(a => a.Kind == ActivityKind.Client && a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration:{subOrchestratorName}").ToList();

        // The client suborchestration activities should be parented to the orchestration activity.
        clientSuborchestrationActivities
            .Should().HaveCount(1)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);
                
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(subOrchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
        
        var clientSuborchestrationActivity = clientSuborchestrationActivities.First();

        var serverSuborchestrationActivities = activities.Where(a => a.Kind == ActivityKind.Server && a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration:{subOrchestratorName}").ToList();

        // The server suborchestration activities should be parented to the client orchestration activity.
        serverSuborchestrationActivities
            .Should().HaveCountGreaterThan(0)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(clientSuborchestrationActivity.Id);
                a.ParentSpanId.Should().Be(clientSuborchestrationActivity.SpanId);
                
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(subOrchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
    }

    [Fact]
    public async Task TaskWithSuborchestrationFailure()
    {
        var activities = new ConcurrentBag<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);
        
        string orchestratorName = nameof(TaskWithSuborchestrationFailure);
        string subOrchestratorName = "SubOrchestration";
        string activityName = nameof(TestActivityAsync);

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    async (ctx, input) =>
                    {
                        await ctx.CallSubOrchestratorAsync(subOrchestratorName, input: false);

                        return true;
                    })
                .AddOrchestratorFunc<bool, bool>(
                    subOrchestratorName,
                    async (ctx, input) =>
                    {
                        await ctx.CallActivityAsync(nameof(TestActivityAsync), input);

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

        await server.DisposeAsync();
        listener.Dispose();

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
                a.Status.Should().Be(ActivityStatusCode.Error);

                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(orchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var clientSuborchestrationActivities = activities.Where(a => a.Kind == ActivityKind.Client && a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration:{subOrchestratorName}").ToList();

        // The client suborchestration activities should be parented to the orchestration activity.
        clientSuborchestrationActivities
            .Should().HaveCount(1)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);
                a.Status.Should().Be(ActivityStatusCode.Error);

                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(subOrchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
        
        var clientSuborchestrationActivity = clientSuborchestrationActivities.First();

        var serverSuborchestrationActivities = activities.Where(a => a.Kind == ActivityKind.Server && a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration:{subOrchestratorName}").ToList();

        // The server suborchestration activities should be parented to the client orchestration activity.
        serverSuborchestrationActivities
            .Should().HaveCountGreaterThan(0)
            .And.AllSatisfy(a =>
            {
                a.ParentId.Should().Be(clientSuborchestrationActivity.Id);
                a.ParentSpanId.Should().Be(clientSuborchestrationActivity.SpanId);
                a.Status.Should().Be(ActivityStatusCode.Error);

                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(subOrchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("orchestration");
            });
    }

    [Fact]
    public async Task TaskOrchestrationWithSentEvent()
    {
        var activities = new ConcurrentBag<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        string orchestratorName = nameof(TaskOrchestrationWithSentEvent);
        string activityName = nameof(TestActivityAsync);
        string targetInstanceId = "TestInstanceId";
        string eventName = "TestEvent";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    (ctx, input) =>
                    {
                        ctx.SendEvent(targetInstanceId, eventName, "TestData");

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

        await server.DisposeAsync();
        listener.Dispose();

        var testActivity = activities.Single(a => a.Source == TestActivitySource && a.OperationName == "Test");
        var createActivity = activities.Single(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"create_orchestration:{orchestratorName}");

        // The creation activity should be parented to the test activity.
        createActivity.ParentId.Should().Be(testActivity.Id);
        createActivity.ParentSpanId.Should().Be(testActivity.SpanId);

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
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var sentEventActivities = activities.Where(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration_event:{eventName}").ToList(); 

        // The "client" (i.e. scheduled) task activities should be parented to the orchestration activity.
        sentEventActivities
            .Should().HaveCount(1)
            .And.AllSatisfy(a =>
            {
                a.Kind.Should().Be(ActivityKind.Producer);
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);

                a.TagObjects.Should().ContainKey("durabletask.event.target_instance_id").WhoseValue.Should().Be(targetInstanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(eventName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("event");
            });
    }

    [Fact]
    public async Task TaskOrchestrationWithTimer()
    {
        var activities = new ConcurrentBag<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        string orchestratorName = nameof(TaskOrchestrationWithTimer);
        string activityName = nameof(TestActivityAsync);

        DateTime fireAt = default;
        
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    async (ctx, input) =>
                    {
                        fireAt = ctx.CurrentUtcDateTime.AddSeconds(1);

                        await ctx.CreateTimer(fireAt, CancellationToken.None);

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

        await server.DisposeAsync();
        listener.Dispose();

        fireAt.Should().NotBe(default);

        var testActivity = activities.Single(a => a.Source == TestActivitySource && a.OperationName == "Test");
        var createActivity = activities.Single(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"create_orchestration:{orchestratorName}");

        // The creation activity should be parented to the test activity.
        createActivity.ParentId.Should().Be(testActivity.Id);
        createActivity.ParentSpanId.Should().Be(testActivity.SpanId);

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
            });
        
        var orchestrationActivity = orchestrationActivities.First();

        var timerActivities = activities.Where(a => a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration:{orchestratorName}:timer").ToList(); 

        // The "client" (i.e. scheduled) task activities should be parented to the orchestration activity.
        timerActivities
            .Should().HaveCount(1)
            .And.AllSatisfy(a =>
            {
                a.Kind.Should().Be(ActivityKind.Internal);
                a.ParentId.Should().Be(orchestrationActivity.Id);
                a.ParentSpanId.Should().Be(orchestrationActivity.SpanId);

                a.TagObjects.Should().ContainKey("durabletask.fire_at").WhoseValue.Should().Be(fireAt.ToString("O"));
                a.TagObjects.Should().ContainKey("durabletask.task.instance_id").WhoseValue.Should().Be(instanceId);
                a.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(orchestratorName);
                a.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("timer");
                a.TagObjects.Should().ContainKey("durabletask.task.task_id").WhoseValue.Should().NotBeNull();
            });
    }

    [Fact]
    public async Task ClientRaiseEvent()
    {
        var activities = new ConcurrentBag<Activity>();

        using var listener = CreateListener(ActivitySourceNames, activities);

        string orchestratorName = nameof(this.ClientRaiseEvent);
        string eventName = "TestEvent";

        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestratorFunc<bool, bool>(
                    orchestratorName,
                    (ctx, input) =>
                    {
                        return true;
                    }));
        });

        string instanceId;

        using (var activity = TestActivitySource.StartActivity("Test"))
        {
            instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: true,
                cancellation: this.TimeoutToken);

            await server.Client.RaiseEventAsync(instanceId, eventName, "TestData", this.TimeoutToken);

            OrchestrationMetadata metadata =
                await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true,
                    this.TimeoutToken);
        }

        await server.DisposeAsync();
        listener.Dispose();

        var testActivity = activities.Single(a => a.Source == TestActivitySource && a.OperationName == "Test");

        var raiseEventActivity = activities.Single(a =>
            a.Source.Name == CoreActivitySourceName && a.OperationName == $"orchestration_event:{eventName}");

        // The raise event activity should be parented to the test activity.
        raiseEventActivity.Kind.Should().Be(ActivityKind.Producer);
        raiseEventActivity.ParentId.Should().Be(testActivity.Id);
        raiseEventActivity.ParentSpanId.Should().Be(testActivity.SpanId);

        raiseEventActivity.TagObjects.Should().ContainKey("durabletask.event.target_instance_id").WhoseValue.Should().Be(instanceId);
        raiseEventActivity.TagObjects.Should().ContainKey("durabletask.task.name").WhoseValue.Should().Be(eventName);
        raiseEventActivity.TagObjects.Should().ContainKey("durabletask.type").WhoseValue.Should().Be("event");
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

            throw new InvalidOperationException("Test activity failed.");
        }
    }
}
