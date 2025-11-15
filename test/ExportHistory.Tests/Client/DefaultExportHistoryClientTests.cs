// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.ExportHistory.Tests.Client;

public class DefaultExportHistoryClientTests
{
    readonly Mock<DurableTaskClient> durableTaskClient;
    readonly Mock<DurableEntityClient> entityClient;
    readonly ILogger<DefaultExportHistoryClient> logger;
    readonly ExportHistoryStorageOptions storageOptions;
    readonly DefaultExportHistoryClient client;

    public DefaultExportHistoryClientTests()
    {
        this.durableTaskClient = new Mock<DurableTaskClient>("test");
        this.entityClient = new Mock<DurableEntityClient>("test");
        this.logger = new TestLogger<DefaultExportHistoryClient>();
        this.storageOptions = new ExportHistoryStorageOptions
        {
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;EndpointSuffix=core.windows.net",
            ContainerName = "test-container",
        };
        this.durableTaskClient.Setup(x => x.Entities).Returns(this.entityClient.Object);
        this.client = new DefaultExportHistoryClient(
            this.durableTaskClient.Object,
            this.logger,
            this.storageOptions);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryClient(
            null!,
            this.logger,
            this.storageOptions);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("durableTaskClient");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryClient(
            this.durableTaskClient.Object,
            null!,
            this.storageOptions);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullStorageOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Action act = () => new DefaultExportHistoryClient(
            this.durableTaskClient.Object,
            this.logger,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("storageOptions");
    }

    [Fact]
    public void GetJobClient_ReturnsValidClient()
    {
        // Arrange
        string jobId = "test-job";

        // Act
        var jobClient = this.client.GetJobClient(jobId);

        // Assert
        jobClient.Should().NotBeNull();
        jobClient.Should().BeOfType<DefaultExportHistoryJobClient>();
    }

    [Fact]
    public async Task CreateJobAsync_WithValidOptions_CreatesJob()
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
        var jobClient = await this.client.CreateJobAsync(options);

        // Assert
        jobClient.Should().NotBeNull();
        this.durableTaskClient.Verify(
            c => c.ScheduleNewOrchestrationInstanceAsync(
                It.Is<TaskName>(n => n.Name == nameof(ExecuteExportJobOperationOrchestrator)),
                It.IsAny<ExportJobOperationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateJobAsync_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Func<Task> act = async () => await this.client.CreateJobAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public async Task GetJobAsync_WhenExists_ReturnsDescription()
    {
        // Arrange
        string jobId = "test-job";
        var state = new ExportJobState
        {
            Status = ExportJobStatus.Active,
            Config = new ExportJobConfiguration(
                ExportMode.Batch,
                new ExportFilter(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
                new ExportDestination("container"),
                ExportFormat.Default),
        };

        var entityInstanceId = new EntityInstanceId(nameof(ExportJob), jobId);

        this.entityClient
            .Setup(c => c.GetEntityAsync<ExportJobState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityMetadata<ExportJobState>(entityInstanceId, state));

        // Act
        var description = await this.client.GetJobAsync(jobId);

        // Assert
        description.Should().NotBeNull();
        description.JobId.Should().Be(jobId);
        description.Status.Should().Be(state.Status);
    }

    [Fact]
    public async Task GetJobAsync_WhenNotExists_ThrowsExportJobNotFoundException()
    {
        // Arrange
        string jobId = "test-job";
        var entityInstanceId = new EntityInstanceId(nameof(ExportJob), jobId);

        this.entityClient
            .Setup(c => c.GetEntityAsync<ExportJobState>(
                It.Is<EntityInstanceId>(id => id.Name == entityInstanceId.Name && id.Key == entityInstanceId.Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityMetadata<ExportJobState>?)null);

        // Act & Assert
        Func<Task> act = async () => await this.client.GetJobAsync(jobId);
        await act.Should().ThrowAsync<ExportJobNotFoundException>()
            .Where(ex => ex.JobId == jobId);
    }

    [Fact]
    public async Task ListJobsAsync_WithNoFilter_ReturnsAllJobs()
    {
        // Arrange
        var state1 = new ExportJobState
        {
            Status = ExportJobStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        var state2 = new ExportJobState
        {
            Status = ExportJobStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var entity1 = new EntityInstanceId(nameof(ExportJob), "job-1");
        var entity2 = new EntityInstanceId(nameof(ExportJob), "job-2");

        var metadata1 = new EntityMetadata<ExportJobState>(entity1, state1);
        var metadata2 = new EntityMetadata<ExportJobState>(entity2, state2);

        var page = new Page<EntityMetadata<ExportJobState>>(
            new List<EntityMetadata<ExportJobState>> { metadata1, metadata2 },
            null);

        this.entityClient
            .Setup(c => c.GetAllEntitiesAsync<ExportJobState>(
                It.IsAny<EntityQuery>()))
            .Returns(Pageable.Create((string? continuation, int? pageSize, CancellationToken cancellation) =>
                Task.FromResult(page)));

        // Act
        var jobs = await this.client.ListJobsAsync().ToListAsync();

        // Assert
        jobs.Should().HaveCount(2);
        jobs.Should().Contain(j => j.JobId == "job-1");
        jobs.Should().Contain(j => j.JobId == "job-2");
    }

    [Fact]
    public async Task ListJobsAsync_WithStatusFilter_FiltersCorrectly()
    {
        // Arrange
        var state1 = new ExportJobState
        {
            Status = ExportJobStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        var state2 = new ExportJobState
        {
            Status = ExportJobStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var entity1 = new EntityInstanceId(nameof(ExportJob), "job-1");
        var entity2 = new EntityInstanceId(nameof(ExportJob), "job-2");

        var metadata1 = new EntityMetadata<ExportJobState>(entity1, state1);
        var metadata2 = new EntityMetadata<ExportJobState>(entity2, state2);

        var page = new Page<EntityMetadata<ExportJobState>>(
            new List<EntityMetadata<ExportJobState>> { metadata1, metadata2 },
            null);

        this.entityClient
            .Setup(c => c.GetAllEntitiesAsync<ExportJobState>(
                It.IsAny<EntityQuery>()))
            .Returns(Pageable.Create((string? continuation, int? pageSize, CancellationToken cancellation) =>
                Task.FromResult(page)));

        var filter = new ExportJobQuery
        {
            Status = ExportJobStatus.Active,
        };

        // Act
        var jobs = await this.client.ListJobsAsync(filter).ToListAsync();

        // Assert
        jobs.Should().HaveCount(1);
        jobs.Should().Contain(j => j.JobId == "job-1" && j.Status == ExportJobStatus.Active);
    }
}

