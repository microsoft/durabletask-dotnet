// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities.Tests;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Entity;

public class ExportJobTests
{
    readonly ExportJob exportJob;
    readonly TestLogger<ExportJob> logger;

    public ExportJobTests()
    {
        this.logger = new TestLogger<ExportJob>();
        this.exportJob = new ExportJob(this.logger);
    }

    [Fact]
    public async Task Create_WithValidOptions_CreatesJob()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var operation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);

        // Act
        await this.exportJob.RunAsync(operation);

        // Assert
        var state = operation.State.GetState(typeof(ExportJobState));
        state.Should().NotBeNull();
        var jobState = Assert.IsType<ExportJobState>(state);
        jobState.Status.Should().Be(ExportJobStatus.Active);
        jobState.Config.Should().NotBeNull();
        jobState.Config!.Mode.Should().Be(ExportMode.Batch);
        jobState.CreatedAt.Should().NotBeNull();
        jobState.LastModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_WithNullOptions_ThrowsInvalidOperationException()
    {
        // Arrange
        var operation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            null);

        // Act & Assert
        // When no input is provided to an entity operation, it throws InvalidOperationException
        // because the entity operation system can't bind the parameter
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.exportJob.RunAsync(operation).AsTask());
        
        exception.Message.Should().Contain("expected an input value");
    }

    [Fact]
    public async Task Get_ReturnsCurrentState()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        var getOperation = new TestEntityOperation(
            nameof(ExportJob.Get),
            createOperation.State,
            null);

        // Act
        var result = await this.exportJob.RunAsync(getOperation);

        // Assert
        result.Should().NotBeNull();
        var state = Assert.IsType<ExportJobState>(result);
        state.Status.Should().Be(ExportJobStatus.Active);
    }

    [Fact]
    public async Task MarkAsCompleted_WhenActive_TransitionsToCompleted()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        var completeOperation = new TestEntityOperation(
            nameof(ExportJob.MarkAsCompleted),
            createOperation.State,
            null);

        // Act
        await this.exportJob.RunAsync(completeOperation);

        // Assert
        var state = completeOperation.State.GetState(typeof(ExportJobState));
        var jobState = Assert.IsType<ExportJobState>(state);
        jobState.Status.Should().Be(ExportJobStatus.Completed);
        jobState.LastError.Should().BeNull();
    }

    [Fact]
    public async Task MarkAsCompleted_WhenNotActive_ThrowsInvalidTransitionException()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        // Mark as failed first
        var failOperation = new TestEntityOperation(
            nameof(ExportJob.MarkAsFailed),
            createOperation.State,
            "test error");
        await this.exportJob.RunAsync(failOperation);

        var completeOperation = new TestEntityOperation(
            nameof(ExportJob.MarkAsCompleted),
            failOperation.State,
            null);

        // Act & Assert
        await Assert.ThrowsAsync<ExportJobInvalidTransitionException>(() =>
            this.exportJob.RunAsync(completeOperation).AsTask());
    }

    [Fact]
    public async Task MarkAsFailed_WhenActive_TransitionsToFailed()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        string errorMessage = "Test error";
        var failOperation = new TestEntityOperation(
            nameof(ExportJob.MarkAsFailed),
            createOperation.State,
            errorMessage);

        // Act
        await this.exportJob.RunAsync(failOperation);

        // Assert
        var state = failOperation.State.GetState(typeof(ExportJobState));
        var jobState = Assert.IsType<ExportJobState>(state);
        jobState.Status.Should().Be(ExportJobStatus.Failed);
        jobState.LastError.Should().Be(errorMessage);
    }

    [Fact]
    public async Task CommitCheckpoint_WithValidRequest_UpdatesState()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        var checkpointRequest = new CommitCheckpointRequest
        {
            ScannedInstances = 100,
            ExportedInstances = 95,
            Checkpoint = new ExportCheckpoint("last-key"),
        };

        var checkpointOperation = new TestEntityOperation(
            nameof(ExportJob.CommitCheckpoint),
            createOperation.State,
            checkpointRequest);

        // Act
        await this.exportJob.RunAsync(checkpointOperation);

        // Assert
        var state = checkpointOperation.State.GetState(typeof(ExportJobState));
        var jobState = Assert.IsType<ExportJobState>(state);
        jobState.ScannedInstances.Should().Be(100);
        jobState.ExportedInstances.Should().Be(95);
        jobState.Checkpoint.Should().NotBeNull();
        jobState.Checkpoint!.LastInstanceKey.Should().Be("last-key");
    }

    [Fact]
    public async Task CommitCheckpoint_WithFailures_MarksJobAsFailed()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        var failures = new List<ExportFailure>
        {
            new("instance-1", "error1", 1, DateTimeOffset.UtcNow),
            new("instance-2", "error2", 2, DateTimeOffset.UtcNow),
        };

        var checkpointRequest = new CommitCheckpointRequest
        {
            ScannedInstances = 0,
            ExportedInstances = 0,
            Checkpoint = null, // No checkpoint means batch failed
            Failures = failures,
        };

        var checkpointOperation = new TestEntityOperation(
            nameof(ExportJob.CommitCheckpoint),
            createOperation.State,
            checkpointRequest);

        // Act
        await this.exportJob.RunAsync(checkpointOperation);

        // Assert
        var state = checkpointOperation.State.GetState(typeof(ExportJobState));
        var jobState = Assert.IsType<ExportJobState>(state);
        jobState.Status.Should().Be(ExportJobStatus.Failed);
        jobState.LastError.Should().NotBeNullOrEmpty();
        jobState.LastError.Should().Contain("Batch export failed");
    }

    [Fact]
    public async Task Delete_ClearsState()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation);

        var deleteOperation = new TestEntityOperation(
            nameof(ExportJob.Delete),
            createOperation.State,
            null);

        // Act
        await this.exportJob.RunAsync(deleteOperation);

        // Assert
        var state = deleteOperation.State.GetState(typeof(ExportJobState));
        state.Should().BeNull();
    }

    [Fact]
    public async Task Create_WhenAlreadyExists_ThrowsInvalidTransitionException()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation1 = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation1);

        var createOperation2 = new TestEntityOperation(
            nameof(ExportJob.Create),
            createOperation1.State,
            options);

        // Act & Assert
        await Assert.ThrowsAsync<ExportJobInvalidTransitionException>(() =>
            this.exportJob.RunAsync(createOperation2).AsTask());
    }

    [Fact]
    public async Task Create_WhenFailed_CanRecreate()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("test-container"));

        var createOperation1 = new TestEntityOperation(
            nameof(ExportJob.Create),
            new TestEntityState(null),
            options);
        await this.exportJob.RunAsync(createOperation1);

        var failOperation = new TestEntityOperation(
            nameof(ExportJob.MarkAsFailed),
            createOperation1.State,
            "test error");
        await this.exportJob.RunAsync(failOperation);

        var createOperation2 = new TestEntityOperation(
            nameof(ExportJob.Create),
            failOperation.State,
            options);

        // Act
        await this.exportJob.RunAsync(createOperation2);

        // Assert
        var state = createOperation2.State.GetState(typeof(ExportJobState));
        var jobState = Assert.IsType<ExportJobState>(state);
        jobState.Status.Should().Be(ExportJobStatus.Active);
    }
}

