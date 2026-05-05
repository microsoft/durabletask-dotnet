// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests for middleware integration in <see cref="DurableTaskTestHost"/>.
/// </summary>
public class MiddlewareTests
{
    const string IllegalAwaitErrorMessage = "An invalid asynchronous invocation was detected. This can be caused by"
        + " awaiting non-durable tasks in an orchestrator function's implementation or by middleware that invokes"
        + " asynchronous code.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Obsolete("Experimental")]
    public async Task Middleware_ContextFields_ArePopulated_WithAndWithoutOrchestrationFilter(
        bool useOrchestrationFilter)
    {
        // Arrange
        const string instanceId = "middleware-context-root";
        var recorder = new MiddlewareRecorder();
        var filter = new RecordingOrchestrationFilter();
        RootMiddlewareInput input = new("tenant-alpha", 7);
        IReadOnlyDictionary<string, string> tags = new Dictionary<string, string>
        {
            ["tenant"] = input.Tenant,
            ["correlation"] = "corr-123",
        };

        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(
            tasks =>
            {
                tasks.AddOrchestrator<RootMiddlewareOrchestrator>();
                tasks.AddOrchestrator<ChildMiddlewareOrchestrator>();
                tasks.AddActivity<MiddlewareActivity>();
            },
            new DurableTaskTestHostOptions
            {
                ConfigureServices = services =>
                {
                    services.AddSingleton(recorder);
                },
                ConfigureWorker = builder =>
                {
                    if (useOrchestrationFilter)
                    {
                        builder.UseOrchestrationFilter(filter);
                    }

                    builder.UseOrchestrationMiddleware<RecordingOrchestrationMiddleware>();
                    builder.UseActivityMiddleware<RecordingActivityMiddleware>();
                },
            });

        // Act
        string scheduledInstanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(RootMiddlewareOrchestrator),
            input,
            new StartOrchestrationOptions(instanceId)
            {
                Tags = tags,
                Version = new TaskVersion("1.0.0"),
            });
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            scheduledInstanceId,
            getInputsAndOutputs: true,
            timeout.Token);

        // Assert
        Assert.Equal(instanceId, scheduledInstanceId);
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        RootMiddlewareOutput output = Assert.IsType<RootMiddlewareOutput>(metadata.ReadOutputAs<RootMiddlewareOutput>());
        Assert.Equal(new RootMiddlewareOutput("root:activity:tenant-alpha:7:child:activity:tenant-alpha:7:8"), output);

        OrchestrationObservation rootObservation = recorder.GetCompletedOrchestration(
            nameof(RootMiddlewareOrchestrator),
            result => result is RootMiddlewareOutput);
        Assert.Equal(new TaskName(nameof(RootMiddlewareOrchestrator)), rootObservation.Name);
        Assert.Equal(instanceId, rootObservation.InstanceId);
        Assert.Equal("1.0.0", rootObservation.Version);
        Assert.Null(rootObservation.Parent);
        Assert.NotNull(rootObservation.Tags);
        Assert.Equal(input.Tenant, rootObservation.Tags!["tenant"]);
        Assert.Equal("corr-123", rootObservation.Tags["correlation"]);
        Assert.True(recorder.HasOrchestration(
            nameof(RootMiddlewareOrchestrator),
            observation => !observation.IsReplaying));
        Assert.Equal(typeof(RootMiddlewareInput), rootObservation.InputType);
        Assert.Equal(input, Assert.IsType<RootMiddlewareInput>(rootObservation.Input));
        Assert.Equal(input, rootObservation.TypedInput);
        Assert.False(string.IsNullOrWhiteSpace(rootObservation.RawInput));
        Assert.Contains(input.Tenant, rootObservation.RawInput);
        Assert.Equal(instanceId, rootObservation.OrchestrationContextInstanceId);
        Assert.NotNull(rootObservation.Features);
        Assert.Same(rootObservation.Features, rootObservation.FeaturesAfterNext);
        Assert.False(rootObservation.CancellationToken.IsCancellationRequested);
        Assert.Equal(output, Assert.IsType<RootMiddlewareOutput>(rootObservation.Result));

        OrchestrationObservation childObservation = recorder.GetCompletedOrchestration(
            nameof(ChildMiddlewareOrchestrator),
            result => result is ChildMiddlewareOutput);
        Assert.Equal(new TaskName(nameof(ChildMiddlewareOrchestrator)), childObservation.Name);
        Assert.NotEqual(instanceId, childObservation.InstanceId);
        Assert.NotNull(childObservation.Parent);
        Assert.Equal(new TaskName(nameof(RootMiddlewareOrchestrator)), childObservation.Parent!.Name);
        Assert.Equal(instanceId, childObservation.Parent.InstanceId);
        Assert.Equal(typeof(ChildMiddlewareInput), childObservation.InputType);
        ChildMiddlewareInput childInput = Assert.IsType<ChildMiddlewareInput>(childObservation.Input);
        Assert.Equal(new ChildMiddlewareInput("activity:tenant-alpha:7", 8), childInput);
        Assert.Equal(childInput, childObservation.TypedInput);
        Assert.False(string.IsNullOrWhiteSpace(childObservation.RawInput));
        Assert.Contains(childInput.Prefix, childObservation.RawInput);
        Assert.Equal(childObservation.InstanceId, childObservation.OrchestrationContextInstanceId);
        Assert.NotNull(childObservation.Features);
        Assert.Same(childObservation.Features, childObservation.FeaturesAfterNext);
        Assert.False(childObservation.CancellationToken.IsCancellationRequested);
        Assert.Equal(new ChildMiddlewareOutput("child:activity:tenant-alpha:7:8"), childObservation.Result);

        ActivityObservation activityObservation = recorder.GetCompletedActivity(nameof(MiddlewareActivity));
        Assert.Equal(new TaskName(nameof(MiddlewareActivity)), activityObservation.Name);
        Assert.Equal(instanceId, activityObservation.InstanceId);
        Assert.Equal(typeof(ActivityMiddlewareInput), activityObservation.InputType);
        ActivityMiddlewareInput activityInput = Assert.IsType<ActivityMiddlewareInput>(activityObservation.Input);
        Assert.Equal(new ActivityMiddlewareInput("tenant-alpha", 7), activityInput);
        Assert.Equal(activityInput, activityObservation.TypedInput);
        Assert.False(string.IsNullOrWhiteSpace(activityObservation.RawInput));
        Assert.Contains(activityInput.Tenant, activityObservation.RawInput);
        Assert.NotNull(activityObservation.ActivityContext);
        Assert.True(activityObservation.ResolvedRecorderFromServices);
        Assert.NotNull(activityObservation.Features);
        Assert.Same(activityObservation.Features, activityObservation.FeaturesAfterNext);
        Assert.False(activityObservation.CancellationToken.IsCancellationRequested);
        Assert.Equal("activity:tenant-alpha:7", activityObservation.Result);

        if (useOrchestrationFilter)
        {
            Assert.True(filter.CallCount >= 2);
            Assert.Contains(filter.Calls, call => call.Name == nameof(RootMiddlewareOrchestrator));
            Assert.Contains(filter.Calls, call => call.Name == nameof(ChildMiddlewareOrchestrator));
        }
        else
        {
            Assert.Equal(0, filter.CallCount);
        }
    }

    [Fact]
    public async Task OrchestrationMiddleware_WithIllegalAwait_FailsOrchestrationWithRuntimeGuardMessage()
    {
        // Arrange
        await using DurableTaskTestHost host = await DurableTaskTestHost.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc<string>(
                    "IllegalAwaitOrchestrator",
                    context => Task.FromResult("should-not-complete"));
            },
            new DurableTaskTestHostOptions
            {
                ConfigureWorker = builder =>
                {
                    builder.UseOrchestrationMiddleware(async (context, next) =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(10));
                        await next(context);
                    });
                },
            });

        // Act
        string instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync("IllegalAwaitOrchestrator");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        OrchestrationMetadata metadata = await host.Client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true,
            timeout.Token);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.NotNull(metadata.FailureDetails);
        Assert.Contains(IllegalAwaitErrorMessage, metadata.FailureDetails!.ErrorMessage);
    }

    sealed class RootMiddlewareOrchestrator : TaskOrchestrator<RootMiddlewareInput, RootMiddlewareOutput>
    {
        public override async Task<RootMiddlewareOutput> RunAsync(
            TaskOrchestrationContext context,
            RootMiddlewareInput input)
        {
            string activityResult = await context.CallActivityAsync<string>(
                nameof(MiddlewareActivity),
                new ActivityMiddlewareInput(input.Tenant, input.Value));
            ChildMiddlewareOutput childResult = await context.CallSubOrchestratorAsync<ChildMiddlewareOutput>(
                nameof(ChildMiddlewareOrchestrator),
                new ChildMiddlewareInput(activityResult, input.Value + 1));

            return new RootMiddlewareOutput($"root:{activityResult}:{childResult.Message}");
        }
    }

    sealed class ChildMiddlewareOrchestrator : TaskOrchestrator<ChildMiddlewareInput, ChildMiddlewareOutput>
    {
        public override Task<ChildMiddlewareOutput> RunAsync(
            TaskOrchestrationContext context,
            ChildMiddlewareInput input)
        {
            return Task.FromResult(new ChildMiddlewareOutput($"child:{input.Prefix}:{input.Value}"));
        }
    }

    sealed class MiddlewareActivity : TaskActivity<ActivityMiddlewareInput, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, ActivityMiddlewareInput input)
        {
            return Task.FromResult($"activity:{input.Tenant}:{input.Value}");
        }
    }

    sealed class RecordingOrchestrationMiddleware : ITaskOrchestrationMiddleware
    {
        readonly MiddlewareRecorder recorder;

        public RecordingOrchestrationMiddleware(MiddlewareRecorder recorder)
        {
            this.recorder = recorder;
        }

        public async Task InvokeAsync(
            TaskOrchestrationMiddlewareContext context,
            TaskOrchestrationMiddlewareDelegate next)
        {
            OrchestrationObservation observation = new(
                context.Name,
                context.InstanceId,
                context.Version,
                context.Parent,
                context.Tags,
                context.IsReplaying,
                context.InputType,
                context.Input,
                GetTypedOrchestrationInput(context),
                context.RawInput,
                context.OrchestrationContext.InstanceId,
                context.Features,
                context.CancellationToken);
            this.recorder.Add(observation);

            await next(context);

            observation.FeaturesAfterNext = context.Features;
            observation.Result = context.Result;
        }
    }

    sealed class RecordingActivityMiddleware : ITaskActivityMiddleware
    {
        readonly MiddlewareRecorder recorder;

        public RecordingActivityMiddleware(MiddlewareRecorder recorder)
        {
            this.recorder = recorder;
        }

        public async Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
        {
            ActivityObservation observation = new(
                context.Name,
                context.InstanceId,
                context.InputType,
                context.Input,
                GetTypedActivityInput(context),
                context.RawInput,
                context.ActivityContext,
                context.Features,
                context.Services.GetService<MiddlewareRecorder>() is not null,
                context.CancellationToken);
            this.recorder.Add(observation);

            await next(context);

            observation.FeaturesAfterNext = context.Features;
            observation.Result = context.Result;
        }
    }

    static object? GetTypedOrchestrationInput(TaskOrchestrationMiddlewareContext context)
    {
        if (context.InputType == typeof(RootMiddlewareInput))
        {
            return context.GetInput<RootMiddlewareInput>();
        }

        if (context.InputType == typeof(ChildMiddlewareInput))
        {
            return context.GetInput<ChildMiddlewareInput>();
        }

        return context.GetInput<object>();
    }

    static object? GetTypedActivityInput(TaskActivityMiddlewareContext context)
    {
        if (context.InputType == typeof(ActivityMiddlewareInput))
        {
            return context.GetInput<ActivityMiddlewareInput>();
        }

        return context.GetInput<object>();
    }

    sealed class MiddlewareRecorder
    {
        readonly ConcurrentQueue<OrchestrationObservation> orchestrations = new();
        readonly ConcurrentQueue<ActivityObservation> activities = new();

        public void Add(OrchestrationObservation observation)
        {
            this.orchestrations.Enqueue(observation);
        }

        public void Add(ActivityObservation observation)
        {
            this.activities.Enqueue(observation);
        }

        public OrchestrationObservation GetCompletedOrchestration(
            string name,
            Func<object?, bool> resultPredicate)
        {
            return this.orchestrations.Last(
                observation => observation.Name == new TaskName(name)
                    && observation.ResultSet
                    && resultPredicate(observation.Result));
        }

        public ActivityObservation GetCompletedActivity(string name)
        {
            return this.activities.Last(
                observation => observation.Name == new TaskName(name)
                    && observation.ResultSet);
        }

        public bool HasOrchestration(string name, Func<OrchestrationObservation, bool> predicate)
        {
            return this.orchestrations.Any(
                observation => observation.Name == new TaskName(name) && predicate(observation));
        }
    }

    [Obsolete("Experimental")]
    sealed class RecordingOrchestrationFilter : IOrchestrationFilter
    {
        readonly ConcurrentQueue<FilterCall> calls = new();
        int callCount;

        public int CallCount => Volatile.Read(ref this.callCount);

        public IEnumerable<FilterCall> Calls => this.calls.ToArray();

        public ValueTask<bool> IsOrchestrationValidAsync(
            OrchestrationFilterParameters info,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this.callCount);
            this.calls.Enqueue(new FilterCall(info.Name, info.Tags));
            return ValueTask.FromResult(true);
        }
    }

    sealed class OrchestrationObservation
    {
        public OrchestrationObservation(
            TaskName name,
            string instanceId,
            string version,
            ParentOrchestrationInstance? parent,
            IReadOnlyDictionary<string, string>? tags,
            bool isReplaying,
            Type inputType,
            object? input,
            object? typedInput,
            string? rawInput,
            string orchestrationContextInstanceId,
            IMiddlewareFeatures features,
            CancellationToken cancellationToken)
        {
            this.Name = name;
            this.InstanceId = instanceId;
            this.Version = version;
            this.Parent = parent;
            this.Tags = tags;
            this.IsReplaying = isReplaying;
            this.InputType = inputType;
            this.Input = input;
            this.TypedInput = typedInput;
            this.RawInput = rawInput;
            this.OrchestrationContextInstanceId = orchestrationContextInstanceId;
            this.Features = features;
            this.CancellationToken = cancellationToken;
        }

        public TaskName Name { get; }

        public string InstanceId { get; }

        public string Version { get; }

        public ParentOrchestrationInstance? Parent { get; }

        public IReadOnlyDictionary<string, string>? Tags { get; }

        public bool IsReplaying { get; }

        public Type InputType { get; }

        public object? Input { get; }

        public object? TypedInput { get; }

        public string? RawInput { get; }

        public string OrchestrationContextInstanceId { get; }

        public IMiddlewareFeatures Features { get; }

        public IMiddlewareFeatures? FeaturesAfterNext { get; set; }

        public CancellationToken CancellationToken { get; }

        public object? Result { get; set; }

        public bool ResultSet => this.FeaturesAfterNext is not null;
    }

    sealed class ActivityObservation
    {
        public ActivityObservation(
            TaskName name,
            string instanceId,
            Type inputType,
            object? input,
            object? typedInput,
            string? rawInput,
            TaskActivityContext activityContext,
            IMiddlewareFeatures features,
            bool resolvedRecorderFromServices,
            CancellationToken cancellationToken)
        {
            this.Name = name;
            this.InstanceId = instanceId;
            this.InputType = inputType;
            this.Input = input;
            this.TypedInput = typedInput;
            this.RawInput = rawInput;
            this.ActivityContext = activityContext;
            this.Features = features;
            this.ResolvedRecorderFromServices = resolvedRecorderFromServices;
            this.CancellationToken = cancellationToken;
        }

        public TaskName Name { get; }

        public string InstanceId { get; }

        public Type InputType { get; }

        public object? Input { get; }

        public object? TypedInput { get; }

        public string? RawInput { get; }

        public TaskActivityContext ActivityContext { get; }

        public IMiddlewareFeatures Features { get; }

        public IMiddlewareFeatures? FeaturesAfterNext { get; set; }

        public bool ResolvedRecorderFromServices { get; }

        public CancellationToken CancellationToken { get; }

        public object? Result { get; set; }

        public bool ResultSet => this.FeaturesAfterNext is not null;
    }

    sealed record FilterCall(string? Name, IReadOnlyDictionary<string, string>? Tags);

    sealed record RootMiddlewareInput(string Tenant, int Value);

    sealed record ActivityMiddlewareInput(string Tenant, int Value);

    sealed record ChildMiddlewareInput(string Prefix, int Value);

    sealed record ChildMiddlewareOutput(string Message);

    sealed record RootMiddlewareOutput(string Message);
}
