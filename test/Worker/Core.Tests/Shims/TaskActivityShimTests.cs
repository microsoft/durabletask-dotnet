// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskActivityShimTests
{
    [Fact]
    public async Task RunAsync_ScheduledWithVersion_ExposesVersionOnContext()
    {
        // Arrange
        CapturingActivity activity = new();
        TaskActivityShim shim = new(
            NullLoggerFactory.Instance,
            new JsonDataConverter(),
            "TestActivity",
            activity);
        TaskContext coreContext = new(
            new OrchestrationInstance { InstanceId = Guid.NewGuid().ToString() },
            "TestActivity",
            "v2",
            taskId: 1);

        // Act
        await shim.RunAsync(coreContext, "null");

        // Assert
        activity.CapturedVersion.Should().Be("v2");
    }

    [Fact]
    public async Task RunAsync_ScheduledWithoutVersion_ExposesEmptyVersionOnContext()
    {
        // Arrange
        CapturingActivity activity = new();
        TaskActivityShim shim = new(
            NullLoggerFactory.Instance,
            new JsonDataConverter(),
            "TestActivity",
            activity);
        TaskContext coreContext = new(
            new OrchestrationInstance { InstanceId = Guid.NewGuid().ToString() },
            "TestActivity",
            version: null,
            taskId: 1);

        // Act
        await shim.RunAsync(coreContext, "null");

        // Assert
        activity.CapturedVersion.Should().Be(string.Empty);
    }

    sealed class CapturingActivity : TaskActivity<object?, object?>
    {
        public string? CapturedVersion { get; private set; }

        public override Task<object?> RunAsync(TaskActivityContext context, object? input)
        {
            this.CapturedVersion = context.Version;
            return Task.FromResult<object?>(null);
        }
    }
}
