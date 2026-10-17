// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using DurableTask.Core.Tracing;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskEntityShimTests
{
    static readonly DistributedTraceContext TraceContext = new(
        "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
        "vendor=value");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleNewOrchestration_WithTags_PreservesStartOptionsAsync(bool optionsOnly)
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
            Version = "v2",
            Tags = tags,
        };
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        EntityBatchResult result = await RunAsync(
            operation => optionsOnly
                ? operation.Context.ScheduleNewOrchestration("Child", options)
                : operation.Context.ScheduleNewOrchestration("Child", "input", options),
            "start");
        DateTimeOffset after = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotNull(result.Actions);
        Assert.NotNull(result.Results);
        StartNewOrchestrationOperationAction action = Assert.IsType<StartNewOrchestrationOperationAction>(
            Assert.Single(result.Actions));
        action.Tags.Should().BeEquivalentTo(tags).And.NotBeSameAs(tags);
        action.Name.Should().Be("Child");
        action.InstanceId.Should().Be("child");
        action.Version.Should().Be("v2");
        action.Input.Should().Be(optionsOnly ? null : "\"input\"");
        action.ScheduledStartTime.Should().Be(startAt.UtcDateTime);
        Assert.InRange(action.RequestTime!.Value, before, after);
        action.ParentTraceContext.Should().BeSameAs(TraceContext);
        Assert.Single(result.Results).Result.Should().Be("\"child\"");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ScheduleNewOrchestration_NullOrEmptyTags_PreservesAbsenceAsync(bool optionsOnly, bool empty)
    {
        // Arrange
        StartOrchestrationOptions options = new("child")
        {
            Tags = empty ? new Dictionary<string, string>() : null!,
        };

        // Act
        EntityBatchResult result = await RunAsync(
            operation => optionsOnly
                ? operation.Context.ScheduleNewOrchestration("Child", options)
                : operation.Context.ScheduleNewOrchestration("Child", input: null, options),
            "start");

        // Assert
        Assert.NotNull(result.Actions);
        StartNewOrchestrationOperationAction action = Assert.IsType<StartNewOrchestrationOperationAction>(
            Assert.Single(result.Actions));
        if (empty)
        {
            Assert.NotNull(action.Tags);
            Assert.Empty(action.Tags);
            Assert.NotSame(options.Tags, action.Tags);
        }
        else
        {
            Assert.Null(action.Tags);
        }
    }

    [Fact]
    public async Task ScheduleNewOrchestration_WithoutOptions_PreservesDefaultsAsync()
    {
        // Arrange
        TaskName name = "Child";

        // Act
        EntityBatchResult result = await RunAsync(
            operation => operation.Context.ScheduleNewOrchestration(name),
            "start");

        // Assert
        Assert.NotNull(result.Actions);
        Assert.NotNull(result.Results);
        StartNewOrchestrationOperationAction action = Assert.IsType<StartNewOrchestrationOperationAction>(
            Assert.Single(result.Actions));
        Assert.Null(action.Tags);
        Assert.Null(action.Input);
        Assert.Null(action.ScheduledStartTime);
        Assert.Equal(string.Empty, action.Version);
        Assert.True(Guid.TryParseExact(action.InstanceId, "N", out _));
        Assert.Single(result.Results).Result.Should().Be(JsonDataConverter.Default.Serialize(action.InstanceId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleNewOrchestration_ReusedDictionary_SnapshotsEachStartAsync(bool optionsOnly)
    {
        // Arrange
        Dictionary<string, string> tags = new() { ["owner"] = "before", ["remove"] = "original" };
        StartOrchestrationOptions options = new("first") { Tags = tags };

        // Act
        EntityBatchResult result = await RunAsync(operation =>
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
        Assert.NotNull(result.Actions);
        Assert.Collection(
            result.Actions,
            first => Assert.IsType<StartNewOrchestrationOperationAction>(first).Tags.Should().BeEquivalentTo(
                new Dictionary<string, string> { ["owner"] = "before", ["remove"] = "original" }),
            second => Assert.IsType<StartNewOrchestrationOperationAction>(second).Tags.Should().BeEquivalentTo(
                new Dictionary<string, string> { ["owner"] = "after", ["added"] = "second" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteOperationBatchAsync_FailedStart_RollsBackOnlyFailedOperationAsync(bool scheduleThrows)
    {
        // Arrange
        string? stateAfterFailure = null;

        // Act
        EntityBatchResult result = await RunAsync(operation =>
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
        Assert.NotNull(result.Results);
        Assert.NotNull(result.Actions);
        Assert.Equal(3, result.Results.Count);
        Assert.Null(result.Results[0].FailureDetails);
        Assert.NotNull(result.Results[1].FailureDetails);
        Assert.Equal(
            scheduleThrows ? typeof(ArgumentException).FullName : typeof(InvalidOperationException).FullName,
            result.Results[1].FailureDetails?.ErrorType);
        Assert.Null(result.Results[2].FailureDetails);
        Assert.Equal("before", stateAfterFailure);
        Assert.Equal("\"after\"", result.EntityState);
        Assert.Collection(
            result.Actions,
            before =>
            {
                StartNewOrchestrationOperationAction action = Assert.IsType<StartNewOrchestrationOperationAction>(before);
                Assert.Equal("before", action.InstanceId);
                action.Tags.Should().BeEquivalentTo(new Dictionary<string, string> { ["operation"] = "before" });
            },
            after =>
            {
                StartNewOrchestrationOperationAction action = Assert.IsType<StartNewOrchestrationOperationAction>(after);
                Assert.Equal("after", action.InstanceId);
                action.Tags.Should().BeEquivalentTo(new Dictionary<string, string> { ["operation"] = "after" });
            });
    }

    [Fact]
    public async Task ScheduleNewOrchestration_NullTagValue_RollsBackWithinOperationAsync()
    {
        // Arrange
        string? stateAfterFailure = null;

        // Act
        EntityBatchResult result = await RunAsync(operation =>
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
        }, "before", "invalid", "after");

        // Assert
        Assert.NotNull(result.Results);
        Assert.NotNull(result.Actions);
        Assert.Null(result.FailureDetails);
        Assert.Equal(3, result.Results.Count);
        Assert.Null(result.Results[0].FailureDetails);
        Assert.NotNull(result.Results[1].FailureDetails);
        Assert.Equal(typeof(ArgumentNullException).FullName, result.Results[1].FailureDetails?.ErrorType);
        Assert.Null(result.Results[2].FailureDetails);
        Assert.Equal("before", stateAfterFailure);
        Assert.Equal("\"after\"", result.EntityState);
        result.Actions.Select(action => Assert.IsType<StartNewOrchestrationOperationAction>(action).InstanceId)
            .Should().Equal("before", "after");
    }

    static Task<EntityBatchResult> RunAsync(Func<TaskEntityOperation, object?> run, params string[] operations)
    {
        TaskEntityShim shim = new(
            JsonDataConverter.Default,
            new TestEntity(run),
            new EntityId("scheduler", "key"),
            NullLogger.Instance);
        return shim.ExecuteOperationBatchAsync(new EntityBatchRequest
        {
            InstanceId = "@scheduler@key",
            EntityState = "\"initial\"",
            Operations = operations.Select(name => new OperationRequest
            {
                Id = Guid.NewGuid(),
                Operation = name,
                TraceContext = TraceContext,
            }).ToList(),
        });
    }

    sealed class TestEntity(Func<TaskEntityOperation, object?> run) : ITaskEntity
    {
        readonly Func<TaskEntityOperation, object?> run = run;

        public ValueTask<object?> RunAsync(TaskEntityOperation operation) => new(this.run(operation));
    }
}
