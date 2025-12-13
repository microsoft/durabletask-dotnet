// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class GrpcDurableTaskClientTests
{
    readonly Mock<ILogger> loggerMock = new();

    GrpcDurableTaskClient CreateClient()
    {
        var callInvoker = Mock.Of<CallInvoker>();
        var options = new GrpcDurableTaskClientOptions
        {
            CallInvoker = callInvoker,
        };

        return new GrpcDurableTaskClient("test", options, this.loggerMock.Object);
    }

    [Fact]
    public async Task ScheduleNewOrchestrationInstanceAsync_InvalidDedupeStatus_ThrowsArgumentException()
    {
        // Arrange
        var client = this.CreateClient();
        var startOptions = new StartOrchestrationOptions
        {
            DedupeStatuses = new[] { "InvalidStatus", "AnotherInvalidStatus" },
        };

        // Act & Assert
        Func<Task> act = async () => await client.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TestOrchestration"),
            input: null,
            startOptions);

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.Message.Should().Contain("Invalid orchestration runtime status: 'InvalidStatus' for deduplication.");
    }

    [Fact]
    public async Task ScheduleNewOrchestrationInstanceAsync_InvalidDedupeStatus_ContainsInvalidStatusInMessage()
    {
        // Arrange
        var client = this.CreateClient();
        var startOptions = new StartOrchestrationOptions
        {
            DedupeStatuses = new[] { "NonExistentStatus" },
        };

        // Act & Assert
        Func<Task> act = async () => await client.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TestOrchestration"),
            input: null,
            startOptions);

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.Message.Should().Contain("'NonExistentStatus'");
        exception.Which.Message.Should().Contain("for deduplication");
    }

    [Fact]
    public async Task ScheduleNewOrchestrationInstanceAsync_MixedValidAndInvalidStatus_ThrowsArgumentException()
    {
        // Arrange
        var client = this.CreateClient();
        var startOptions = new StartOrchestrationOptions
        {
            DedupeStatuses = new[] { "Completed", "InvalidStatus", "Failed" },
        };

        // Act & Assert
        Func<Task> act = async () => await client.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TestOrchestration"),
            input: null,
            startOptions);

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.Message.Should().Contain("Invalid orchestration runtime status: 'InvalidStatus' for deduplication.");
    }

    [Fact]
    public async Task ScheduleNewOrchestrationInstanceAsync_CaseInsensitiveValidStatus_DoesNotThrowArgumentException()
    {
        // Arrange
        var client = this.CreateClient();
        var startOptions = new StartOrchestrationOptions
        {
            DedupeStatuses = new[] { "completed", "FAILED", "Terminated" },
        };

        // Act & Assert - Case-insensitive parsing should work, so no ArgumentException should be thrown
        // The call will fail at the gRPC level, but validation should pass
        Func<Task> act = async () => await client.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TestOrchestration"),
            input: null,
            startOptions);

        // Should not throw ArgumentException for invalid status (case-insensitive parsing works)
        // It may throw other exceptions due to gRPC call failure, but not ArgumentException
        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Should().NotBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task ScheduleNewOrchestrationInstanceAsync_ValidDedupeStatus_DoesNotThrowArgumentException()
    {
        // Arrange
        var client = this.CreateClient();
        var startOptions = new StartOrchestrationOptions
        {
            DedupeStatuses = new[] { "Completed", "Failed" },
        };

        // Act & Assert - Valid statuses should pass validation
        // The call will fail at the gRPC level, but validation should pass
        Func<Task> act = async () => await client.ScheduleNewOrchestrationInstanceAsync(
            new TaskName("TestOrchestration"),
            input: null,
            startOptions);

        // Should not throw ArgumentException for invalid status since "Completed" and "Failed" are valid
        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Should().NotBeOfType<ArgumentException>();
    }
}

