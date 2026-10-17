// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Caching.Memory;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class GrpcEntityRunnerTagTests
{
    static readonly P.TraceContext TraceContext = new()
    {
        TraceParent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
        TraceState = "vendor=value",
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAndRunAsync_TaggedStart_PreservesOptionsInResponseAsync(bool optionsOnly)
    {
        // Arrange
        Dictionary<string, string> tags = new()
        {
            ["owner"] = "entity",
            ["\u6807\u7b7e"] = "\u4f60\u597d \ud83c\udf0d",
            ["empty"] = string.Empty,
            [string.Empty] = "empty-key",
        };
        DateTimeOffset startAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
        StartOrchestrationOptions options = new("child", startAt)
        {
            Tags = tags,
            Version = "v2",
        };
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        P.EntityBatchResult result = await RunAsync(
            operation => optionsOnly
                ? operation.Context.ScheduleNewOrchestration("Child", options)
                : operation.Context.ScheduleNewOrchestration("Child", "input", options),
            "start");
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Assert
        Assert.Null(result.FailureDetails);
        P.StartNewOrchestrationAction action = Assert.Single(result.Actions).StartNewOrchestration;
        action.Tags.Should().BeEquivalentTo(tags);
        Assert.Equal("Child", action.Name);
        Assert.Equal("child", action.InstanceId);
        Assert.Equal("v2", action.Version);
        Assert.Equal(optionsOnly ? null : "\"input\"", action.Input);
        Assert.Equal(startAt.UtcDateTime, action.ScheduledTime.ToDateTime());
        Assert.InRange(action.RequestTime.ToDateTimeOffset(), before, after);
        Assert.Equal(TraceContext, action.ParentTraceContext);
        Assert.Equal("\"child\"", Assert.Single(result.Results).Success.Result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoadAndRunAsync_NullOrEmptyTags_EmitsEmptyMapAsync(bool optionsOnly, bool empty)
    {
        // Arrange
        StartOrchestrationOptions options = new("child")
        {
            Tags = empty ? new Dictionary<string, string>() : null!,
        };

        // Act
        P.EntityBatchResult result = await RunAsync(
            operation => optionsOnly
                ? operation.Context.ScheduleNewOrchestration("Child", options)
                : operation.Context.ScheduleNewOrchestration("Child", input: null, options),
            "start");

        // Assert
        Assert.Empty(Assert.Single(result.Actions).StartNewOrchestration.Tags);
        Assert.NotNull(Assert.Single(result.Results).Success);
    }

    [Fact]
    public async Task LoadAndRunAsync_WithoutOptions_PreservesDefaultsAsync()
    {
        // Arrange
        TaskName name = "Child";

        // Act
        P.EntityBatchResult result = await RunAsync(
            operation => operation.Context.ScheduleNewOrchestration(name),
            "start");

        // Assert
        P.StartNewOrchestrationAction action = Assert.Single(result.Actions).StartNewOrchestration;
        Assert.Empty(action.Tags);
        Assert.Null(action.Input);
        Assert.Null(action.ScheduledTime);
        Assert.Equal(string.Empty, action.Version);
        Assert.True(Guid.TryParseExact(action.InstanceId, "N", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAndRunAsync_ReusedDictionary_PreservesEachSnapshotAsync(bool optionsOnly)
    {
        // Arrange
        Dictionary<string, string> tags = new() { ["owner"] = "before", ["remove"] = "original" };
        StartOrchestrationOptions options = new("first") { Tags = tags };

        // Act
        P.EntityBatchResult result = await RunAsync(operation =>
        {
            if (optionsOnly)
            {
                operation.Context.ScheduleNewOrchestration("Child", options);
            }
            else
            {
                operation.Context.ScheduleNewOrchestration("Child", "first-input", options);
            }

            tags["owner"] = "after";
            tags.Remove("remove");
            tags["added"] = "second";
            options = options with { InstanceId = "second" };
            if (optionsOnly)
            {
                operation.Context.ScheduleNewOrchestration("Child", options);
            }
            else
            {
                operation.Context.ScheduleNewOrchestration("Child", "second-input", options);
            }

            tags.Clear();
            return null;
        }, "start");

        // Assert
        Assert.Collection(
            result.Actions,
            first => first.StartNewOrchestration.Tags.Should().BeEquivalentTo(
                new Dictionary<string, string> { ["owner"] = "before", ["remove"] = "original" }),
            second => second.StartNewOrchestration.Tags.Should().BeEquivalentTo(
                new Dictionary<string, string> { ["owner"] = "after", ["added"] = "second" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAndRunAsync_FailedStart_DiscardsOnlyFailedOperationActionsAsync(bool scheduleThrows)
    {
        // Arrange
        string? stateAfterFailure = null;

        // Act
        P.EntityBatchResult result = await RunAsync(operation =>
        {
            if (operation.Name == "after")
            {
                stateAfterFailure = operation.State.GetState<string>();
            }

            operation.State.SetState(operation.Name);
            operation.Context.ScheduleNewOrchestration("Child", new StartOrchestrationOptions(operation.Name)
            {
                Tags = new Dictionary<string, string> { ["operation"] = operation.Name },
            });
            if (operation.Name == "fail")
            {
                if (scheduleThrows)
                {
                    operation.Context.ScheduleNewOrchestration("Child", new StartOrchestrationOptions("@entity@key"));
                }

                throw new InvalidOperationException("Failure after scheduling.");
            }

            return null;
        }, "before", "fail", "after");

        // Assert
        Assert.Null(result.FailureDetails);
        Assert.Equal(3, result.Results.Count);
        Assert.NotNull(result.Results[0].Success);
        Assert.NotNull(result.Results[1].Failure);
        Assert.Equal(
            scheduleThrows ? typeof(ArgumentException).FullName : typeof(InvalidOperationException).FullName,
            result.Results[1].Failure.FailureDetails.ErrorType);
        Assert.NotNull(result.Results[2].Success);
        Assert.Equal("before", stateAfterFailure);
        Assert.Equal("\"after\"", result.EntityState);
        Assert.Collection(
            result.Actions,
            before =>
            {
                Assert.Equal("before", before.StartNewOrchestration.InstanceId);
                before.StartNewOrchestration.Tags.Should().BeEquivalentTo(
                    new Dictionary<string, string> { ["operation"] = "before" });
            },
            after =>
            {
                Assert.Equal("after", after.StartNewOrchestration.InstanceId);
                after.StartNewOrchestration.Tags.Should().BeEquivalentTo(
                    new Dictionary<string, string> { ["operation"] = "after" });
            });
    }

    [Fact]
    public async Task LoadAndRunAsync_NullTagValue_RollsBackBeforeSerializationAndCachingAsync()
    {
        // Arrange
        using ExtendedSessionsCache cache = new();
        string? stateAfterFailure = null;

        // Act
        P.EntityBatchResult result = await RunAsync(operation =>
        {
            if (operation.Name == "after")
            {
                stateAfterFailure = operation.State.GetState<string>();
            }

            operation.State.SetState(operation.Name);
            operation.Context.ScheduleNewOrchestration("Child", new StartOrchestrationOptions(operation.Name)
            {
                Tags = new Dictionary<string, string> { ["operation"] = operation.Name },
            });
            if (operation.Name == "invalid")
            {
                operation.Context.ScheduleNewOrchestration("Child", new StartOrchestrationOptions("invalid-tags")
                {
                    Tags = new Dictionary<string, string> { ["valid"] = string.Empty, ["invalid"] = null! },
                });
            }

            return null;
        }, cache, "before", "invalid", "after");

        // Assert
        Assert.Null(result.FailureDetails);
        Assert.Equal(3, result.Results.Count);
        Assert.NotNull(result.Results[0].Success);
        Assert.NotNull(result.Results[1].Failure);
        Assert.Equal(typeof(ArgumentNullException).FullName, result.Results[1].Failure.FailureDetails.ErrorType);
        Assert.NotNull(result.Results[2].Success);
        Assert.Equal("before", stateAfterFailure);
        Assert.Equal("\"after\"", result.EntityState);
        result.Actions.Select(action => action.StartNewOrchestration.InstanceId).Should().Equal("before", "after");
        Assert.True(cache.GetOrInitializeCache(30).TryGetValue("@scheduler@key", out string? cachedState));
        Assert.Equal("\"after\"", cachedState);
    }

    static Task<P.EntityBatchResult> RunAsync(Func<TaskEntityOperation, object?> run, params string[] operations)
        => RunAsync(run, null, operations);

    static async Task<P.EntityBatchResult> RunAsync(
        Func<TaskEntityOperation, object?> run, ExtendedSessionsCache? cache, params string[] operations)
    {
        P.EntityBatchRequest request = new()
        {
            InstanceId = "@scheduler@key",
            EntityState = "\"initial\"",
            Operations =
            {
                operations.Select(name => new P.OperationRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    Operation = name,
                    TraceContext = TraceContext,
                }),
            },
        };
        request.Properties.Add("IncludeState", Value.ForBool(true));
        if (cache is not null)
        {
            request.Properties.Add("IsExtendedSession", Value.ForBool(true));
            request.Properties.Add("ExtendedSessionIdleTimeoutInSeconds", Value.ForNumber(30));
        }

        string encodedRequest = Convert.ToBase64String(request.ToByteArray());
        TestEntity entity = new(run);
        string response = cache is null
            ? await GrpcEntityRunner.LoadAndRunAsync(encodedRequest, entity)
            : await GrpcEntityRunner.LoadAndRunAsync(encodedRequest, entity, cache);
        return P.EntityBatchResult.Parser.ParseFrom(Convert.FromBase64String(response));
    }

    sealed class TestEntity(Func<TaskEntityOperation, object?> run) : ITaskEntity
    {
        readonly Func<TaskEntityOperation, object?> run = run;

        public ValueTask<object?> RunAsync(TaskEntityOperation operation) => new(this.run(operation));
    }
}
