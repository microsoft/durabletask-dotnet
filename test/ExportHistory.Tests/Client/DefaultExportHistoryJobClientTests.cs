// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Client;

public class DefaultExportHistoryJobClientTests
{
    readonly Mock<DurableTaskClient> durableTaskClient;
    readonly Mock<DurableEntityClient> entityClient;
    readonly ILogger logger;
    readonly ExportHistoryStorageOptions storageOptions;
    readonly string jobId = "test-job-123";
    readonly DefaultExportHistoryJobClient client;

    public DefaultExportHistoryJobClientTests()
    {
        this.durableTaskClient = new Mock<DurableTaskClient>("test");
        this.entityClient = new Mock<DurableEntityClient>("test");
        this.logger = new TestLogger();
        this.storageOptions = new ExportHistoryStorageOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net",
            ContainerName = "test-container",
        };
        this.durableTaskClient.Setup(x => x.Entities).Returns(this.entityClient.Object);
        this.client = new DefaultExportHistoryJobClient(
            this.durableTaskClient.Object,
            this.jobId,
            this.logger,
            this.storageOptions);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryJobClient(
            null!,
            this.jobId,
            this.logger,
            this.storageOptions);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("durableTaskClient");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_WithInvalidJobId_ThrowsArgumentException(string? invalidJobId)
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryJobClient(
            this.durableTaskClient.Object,
            invalidJobId!,
            this.logger,
            this.storageOptions);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithWhitespaceJobId_DoesNotThrow()
    {
        // Arrange
        // Check.NotNullOrEmpty only checks for null, empty, or strings starting with '\0'
        // It does NOT check for whitespace-only strings, so "   " is valid
        string testJobId = "   ";

        // Act
        var client = new DefaultExportHistoryJobClient(
            this.durableTaskClient.Object,
            testJobId,
            this.logger,
            this.storageOptions);

        // Assert
        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryJobClient(
            this.durableTaskClient.Object,
            this.jobId,
            null!,
            this.storageOptions);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullStorageOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryJobClient(
            this.durableTaskClient.Object,
            this.jobId,
            this.logger,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("storageOptions");
    }

    [Fact]
    public async Task DescribeAsync_WhenExists_ReturnsDescription()
    {
        // Arrange
        var state = new ExportJobState
        {
            Status = ExportJobStatus.Active,
            Config = new ExportJobConfiguration(
                ExportMode.Batch,
                new ExportFilter(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
                new ExportDestination("container"),
                ExportFormat.Default),
        };

        var entityInstanceId = new EntityInstanceId(nameof(ExportJob), this.jobId);

        this.entityClient
            .Setup(c => c.GetEntityAsync<ExportJobState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityMetadata<ExportJobState>(entityInstanceId, state));

        // Act
        var description = await this.client.DescribeAsync();

        // Assert
        description.Should().NotBeNull();
        description.JobId.Should().Be(this.jobId);
        description.Status.Should().Be(state.Status);
        description.Config.Should().Be(state.Config);
    }

    [Fact]
    public async Task DescribeAsync_WhenNotExists_ThrowsExportJobNotFoundException()
    {
        // Arrange
        var entityInstanceId = new EntityInstanceId(nameof(ExportJob), this.jobId);

        this.entityClient
            .Setup(c => c.GetEntityAsync<ExportJobState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityMetadata<ExportJobState>?)null);

        // Act & Assert
        Func<Task> act = async () => await this.client.DescribeAsync();
        await act.Should().ThrowAsync<ExportJobNotFoundException>()
            .Where(ex => ex.JobId == this.jobId);
    }

    [Fact]
    public async Task CreateAsync_WithValidOptions_CreatesJob()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("container"));
        string instanceId = "test-instance";

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteExportJobOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
            });

        // Act
        await this.client.CreateAsync(options);

        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        this.durableTaskClient.Verify(
            c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Func<Task> act = async () => await this.client.CreateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public async Task CreateAsync_WhenOrchestrationFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new ExportJobCreationOptions(
            ExportMode.Batch,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            new ExportDestination("container"));
        string instanceId = "test-instance";
        string errorMessage = "Test error message";

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteExportJobOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Failed,
                FailureDetails = new TaskFailureDetails("TestError", errorMessage, null, null, null),
            });

        // Act & Assert
        Func<Task> act = async () => await this.client.CreateAsync(options);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*Failed to create export job '{this.jobId}'*");
    }

    [Fact]
    public async Task DeleteAsync_ExecutesDeleteOperation()
    {
        // Arrange
        string instanceId = "test-instance";
        string orchestratorInstanceId = ExportHistoryConstants.GetOrchestratorInstanceId(this.jobId);

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteExportJobOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
            });

        this.durableTaskClient
            .Setup(c => c.TerminateInstanceAsync(orchestratorInstanceId, It.IsAny<TerminateInstanceOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(orchestratorInstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExportJobOrchestrator), orchestratorInstanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Terminated,
            });

        this.durableTaskClient
            .Setup(c => c.PurgeInstanceAsync(orchestratorInstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PurgeResult(1));

        // Act
        await this.client.DeleteAsync();

        // Assert
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenOrchestrationNotFound_HandlesGracefully()
    {
        // Arrange
        string instanceId = "test-instance";
        string orchestratorInstanceId = ExportHistoryConstants.GetOrchestratorInstanceId(this.jobId);

        this.durableTaskClient
            .Setup(c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);

        this.durableTaskClient
            .Setup(c => c.WaitForInstanceCompletionAsync(instanceId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationMetadata(nameof(ExecuteExportJobOperationOrchestrator), instanceId)
            {
                RuntimeStatus = OrchestrationRuntimeStatus.Completed,
            });

        this.durableTaskClient
            .Setup(c => c.TerminateInstanceAsync(orchestratorInstanceId, It.IsAny<TerminateInstanceOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "Not found")));

        // Act
        await this.client.DeleteAsync();

        // Assert - Should not throw
        this.durableTaskClient.Verify(
            c => c.TerminateInstanceAsync(orchestratorInstanceId, It.IsAny<TerminateInstanceOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

