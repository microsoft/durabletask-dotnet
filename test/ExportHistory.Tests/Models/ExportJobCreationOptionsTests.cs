// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Client;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Models;

public class ExportJobCreationOptionsTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        // Act
        var options = new ExportJobCreationOptions();

        // Assert
        options.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_BatchMode_WithValidParameters_CreatesInstance()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination);

        // Assert
        options.Should().NotBeNull();
        options.Mode.Should().Be(ExportMode.Batch);
        options.CompletedTimeFrom.Should().Be(from);
        options.CompletedTimeTo.Should().Be(to);
        options.Destination.Should().Be(destination);
        options.JobId.Should().NotBeNullOrEmpty();
        options.Format.Should().Be(ExportFormat.Default);
        options.RuntimeStatus.Should().NotBeNull();
        options.MaxInstancesPerBatch.Should().Be(100);
    }

    [Fact]
    public void Constructor_BatchMode_WithCustomJobId_CreatesInstance()
    {
        // Arrange
        string jobId = "custom-job-id";
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            jobId);

        // Assert
        options.JobId.Should().Be(jobId);
    }

    [Fact]
    public void Constructor_BatchMode_WithDefaultJobId_GeneratesGuid()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null);

        // Assert
        options.JobId.Should().NotBeNullOrEmpty();
        Guid.TryParse(options.JobId, out _).Should().BeTrue();
    }

    [Fact]
    public void Constructor_BatchMode_WithEmptyJobId_GeneratesGuid()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            string.Empty);

        // Assert
        options.JobId.Should().NotBeNullOrEmpty();
        Guid.TryParse(options.JobId, out _).Should().BeTrue();
    }

    [Fact]
    public void Constructor_BatchMode_WithDefaultCompletedTimeFrom_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = default;
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Batch, from, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeFrom is required for Batch export mode*");
    }

    [Fact]
    public void Constructor_BatchMode_WithNullCompletedTimeTo_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset? to = null;
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Batch, from, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeTo is required for Batch export mode*");
    }

    [Fact]
    public void Constructor_BatchMode_WithCompletedTimeToLessThanFrom_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow;
        DateTimeOffset to = DateTimeOffset.UtcNow.AddDays(-1);
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Batch, from, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeTo* must be greater than CompletedTimeFrom*");
    }

    [Fact]
    public void Constructor_BatchMode_WithCompletedTimeToEqualToFrom_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = from;
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Batch, from, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeTo* must be greater than CompletedTimeFrom*");
    }

    [Fact]
    public void Constructor_BatchMode_WithCompletedTimeToInFuture_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow.AddDays(1);
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Batch, from, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeTo* cannot be in the future*");
    }

    [Fact]
    public void Constructor_ContinuousMode_WithValidParameters_CreatesInstance()
    {
        // Arrange
        ExportDestination destination = new("test-container");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Continuous,
            default,
            null,
            destination);

        // Assert
        options.Should().NotBeNull();
        options.Mode.Should().Be(ExportMode.Continuous);
        options.CompletedTimeFrom.Should().Be(default);
        options.CompletedTimeTo.Should().BeNull();
        options.Destination.Should().Be(destination);
    }

    [Fact]
    public void Constructor_ContinuousMode_WithCompletedTimeFrom_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Continuous, from, null, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeFrom is not allowed for Continuous export mode*");
    }

    [Fact]
    public void Constructor_ContinuousMode_WithCompletedTimeTo_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(ExportMode.Continuous, default, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CompletedTimeTo is not allowed for Continuous export mode*");
    }

    [Fact]
    public void Constructor_WithInvalidMode_ThrowsArgumentException()
    {
        // Arrange
        ExportMode invalidMode = (ExportMode)999;
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(invalidMode, from, to, destination);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid export mode*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    [InlineData(2000)]
    public void Constructor_WithInvalidMaxInstancesPerBatch_ThrowsArgumentOutOfRangeException(int maxInstances)
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        Action act = () => new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            null,
            null,
            maxInstances);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*MaxInstancesPerBatch must be between 1 and 1000*");
    }

    [Fact]
    public void Constructor_WithValidMaxInstancesPerBatch_CreatesInstance()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");
        int maxInstances = 500;

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            null,
            null,
            maxInstances);

        // Assert
        options.MaxInstancesPerBatch.Should().Be(maxInstances);
    }

    [Fact]
    public void Constructor_WithNonTerminalRuntimeStatus_ThrowsArgumentException()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");
        List<OrchestrationRuntimeStatus> runtimeStatus = new()
        {
            OrchestrationRuntimeStatus.Running, // Not terminal
        };

        // Act
        Action act = () => new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            null,
            runtimeStatus);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Export supports terminal orchestration statuses only*");
    }

    [Fact]
    public void Constructor_WithTerminalRuntimeStatus_CreatesInstance()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");
        List<OrchestrationRuntimeStatus> runtimeStatus = new()
        {
            OrchestrationRuntimeStatus.Completed,
            OrchestrationRuntimeStatus.Failed,
            OrchestrationRuntimeStatus.Terminated,
            OrchestrationRuntimeStatus.ContinuedAsNew,
        };

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            null,
            runtimeStatus);

        // Assert
        options.RuntimeStatus.Should().BeEquivalentTo(runtimeStatus);
    }

    [Fact]
    public void Constructor_WithNullRuntimeStatus_UsesDefaultTerminalStatuses()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            null,
            null);

        // Assert
        options.RuntimeStatus.Should().NotBeNull();
        options.RuntimeStatus.Should().HaveCount(4);
        options.RuntimeStatus.Should().Contain(OrchestrationRuntimeStatus.Completed);
        options.RuntimeStatus.Should().Contain(OrchestrationRuntimeStatus.Failed);
        options.RuntimeStatus.Should().Contain(OrchestrationRuntimeStatus.Terminated);
        options.RuntimeStatus.Should().Contain(OrchestrationRuntimeStatus.ContinuedAsNew);
    }

    [Fact]
    public void Constructor_WithEmptyRuntimeStatus_UsesDefaultTerminalStatuses()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");
        List<OrchestrationRuntimeStatus> runtimeStatus = new();

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            null,
            runtimeStatus);

        // Assert
        options.RuntimeStatus.Should().NotBeNull();
        options.RuntimeStatus.Should().HaveCount(4);
    }

    [Fact]
    public void Constructor_WithCustomFormat_CreatesInstance()
    {
        // Arrange
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset to = DateTimeOffset.UtcNow;
        ExportDestination destination = new("test-container");
        ExportFormat format = new("json", "2.0");

        // Act
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            from,
            to,
            destination,
            null,
            format);

        // Assert
        options.Format.Should().Be(format);
    }
}

