// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

public class AlternateHostPurgeClientTests
{
    [Fact]
    public async Task Enable_UsesExistingServiceClient_AndRepeatedSetupIsIndependentPerHubAsync()
    {
        // Arrange
        Hub first = new("first");
        Hub second = new("second");
        ServiceCollection services = new();
        first.Register(services);
        second.Register(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IDurableTaskClientProvider clients = provider.GetRequiredService<IDurableTaskClientProvider>();
        using CancellationTokenSource cancellation = new();

        // Act
        await clients.GetClient("first").SetLargePayloadAutoPurgeAsync(first.Purge.Object, true, 123, cancellation.Token);
        await clients.GetClient("second").SetLargePayloadAutoPurgeAsync(second.Purge.Object, true, 456, cancellation.Token);
        first.AlreadyExists = true;
        await clients.GetClient("first").SetLargePayloadAutoPurgeAsync(first.Purge.Object, true, 789, cancellation.Token);

        // Assert
        Assert.Equal(new[] { "Set:True", "Start", "Wait", "Event", "Set:True", "Start", "Wait", "Event" }, first.Calls);
        Assert.Equal(new[] { "Set:True", "Start", "Wait", "Event" }, second.Calls);
        Assert.Equal(new[] { "123", "789" }, first.Events.Select(e => e.Input));
        Assert.Equal("456", Assert.Single(second.Events).Input);
        Assert.All(first.Tokens.Concat(second.Tokens), token => Assert.Equal(cancellation.Token, token));
        Assert.All(first.Starts.Concat(second.Starts), start =>
        {
            Assert.Equal(nameof(BlobPurgeJobOrchestrator), start.Name);
            Assert.Equal(BlobPurgeConstants.OrchestratorInstanceId, start.OrchestrationInstance.InstanceId);
            Assert.Equal(string.Empty, start.Version);
            Assert.InRange(JsonDataConverter.Default.Deserialize<BlobPurgeJobRunRequest>(start.Input)!.PurgeBatchSize, 1, 1000);
        });
    }

    [Fact]
    public async Task Disable_OnlyWritesSetting_AndStandaloneOverloadStillRejectsShimAsync()
    {
        // Arrange
        Hub hub = new("hub");
        ServiceCollection services = new();
        hub.Register(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskClient client = provider.GetRequiredService<IDurableTaskClientProvider>().GetClient("hub");

        // Act
        await client.SetLargePayloadAutoPurgeAsync(hub.Purge.Object, false, -1);

        // Assert
        Assert.Equal(new[] { "Set:False" }, hub.Calls);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.SetLargePayloadAutoPurgeAsync(true));
        Assert.Equal(new[] { "Set:False" }, hub.Calls);
    }

    [Theory]
    [InlineData(OrchestrationStatus.Suspended, nameof(BlobPurgeJobOrchestrator))]
    [InlineData(OrchestrationStatus.Pending, nameof(BlobPurgeJobOrchestrator))]
    [InlineData(OrchestrationStatus.ContinuedAsNew, nameof(BlobPurgeJobOrchestrator))]
    [InlineData(OrchestrationStatus.Running, "BusinessOrchestration")]
    public async Task Enable_LiveCollisionOrNonRunningState_DoesNotSendEventAsync(OrchestrationStatus status, string name)
    {
        // Arrange
        Hub hub = new("hub") { AlreadyExists = true, Status = status, Name = name };
        ServiceCollection services = new();
        hub.Register(services);
        await using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskClient client = provider.GetRequiredService<IDurableTaskClientProvider>().GetClient("hub");

        // Act / Assert
        // The real shim waits on Pending, so cancellation bounds that case rather than changing its semantics.
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
        Exception? error = await Record.ExceptionAsync(() =>
            client.SetLargePayloadAutoPurgeAsync(hub.Purge.Object, true, cancellationToken: cancellation.Token));
        Assert.NotNull(error);
        Assert.True(error is InvalidOperationException or OperationCanceledException, error.ToString());
        Assert.Empty(hub.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task InvalidBatch_DoesNotWriteSettingAsync(int batchSize)
    {
        // Arrange
        Mock<DurableTaskClient> client = new(MockBehavior.Strict, "hub");
        Mock<ILargePayloadPurgeClient> purge = new(MockBehavior.Strict);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.Object.SetLargePayloadAutoPurgeAsync(purge.Object, true, batchSize));
        client.VerifyNoOtherCalls();
        purge.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SettingFailure_PropagatesWithoutStartingAsync()
    {
        // Arrange
        Mock<DurableTaskClient> client = new(MockBehavior.Strict, "hub");
        Mock<ILargePayloadPurgeClient> purge = new(MockBehavior.Strict);
        using CancellationTokenSource cancellation = new();
        OperationCanceledException failure = new(cancellation.Token);
        purge.Setup(p => p.SetLargePayloadAutoPurgeAsync(true, cancellation.Token)).ThrowsAsync(failure);

        // Act / Assert
        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            client.Object.SetLargePayloadAutoPurgeAsync(purge.Object, true, cancellationToken: cancellation.Token)));
        client.VerifyNoOtherCalls();
    }

    sealed class Hub
    {
        readonly string key;
        readonly Mock<IOrchestrationServiceClient> service = new(MockBehavior.Strict);

        public Hub(string key)
        {
            this.key = key;
            this.Purge.Setup(p => p.SetLargePayloadAutoPurgeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<bool, CancellationToken>((enabled, token) =>
                {
                    this.Calls.Add($"Set:{enabled}");
                    this.Tokens.Add(token);
                }).Returns(Task.CompletedTask);
            this.service.Setup(s => s.CreateTaskOrchestrationAsync(It.IsAny<TaskMessage>(), It.IsAny<OrchestrationStatus[]>()))
                .Returns((TaskMessage message, OrchestrationStatus[] statuses) =>
                {
                    this.Calls.Add("Start");
                    Assert.Equal(new[] { OrchestrationStatus.Running, OrchestrationStatus.Pending, OrchestrationStatus.Suspended, OrchestrationStatus.ContinuedAsNew }, statuses);
                    this.Starts.Add(Assert.IsType<ExecutionStartedEvent>(message.Event));
                    return this.AlreadyExists ? Task.FromException(new OrchestrationAlreadyExistsException("existing")) : Task.CompletedTask;
                });
            this.service.Setup(s => s.GetOrchestrationStateAsync(BlobPurgeConstants.OrchestratorInstanceId, false))
                .Returns(() =>
                {
                    this.Calls.Add("Wait");
                    return Task.FromResult<IList<OrchestrationState>>([new()
                    {
                        Name = this.Name,
                        OrchestrationStatus = this.Status,
                        OrchestrationInstance = new() { InstanceId = BlobPurgeConstants.OrchestratorInstanceId },
                    }]);
                });
            this.service.Setup(s => s.SendTaskOrchestrationMessageAsync(It.IsAny<TaskMessage>()))
                .Callback<TaskMessage>(message =>
                {
                    this.Calls.Add("Event");
                    Assert.Equal(BlobPurgeConstants.OrchestratorInstanceId, message.OrchestrationInstance.InstanceId);
                    EventRaisedEvent raised = Assert.IsType<EventRaisedEvent>(message.Event);
                    Assert.Equal(BlobPurgeConstants.SetBatchSizeEvent, raised.Name);
                    this.Events.Add(raised);
                }).Returns(Task.CompletedTask);
        }

        public Mock<ILargePayloadPurgeClient> Purge { get; } = new(MockBehavior.Strict);
        public List<string> Calls { get; } = [];
        public List<ExecutionStartedEvent> Starts { get; } = [];
        public List<EventRaisedEvent> Events { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public bool AlreadyExists { get; set; }
        public OrchestrationStatus Status { get; init; } = OrchestrationStatus.Running;
        public string Name { get; init; } = nameof(BlobPurgeJobOrchestrator);

        public void Register(IServiceCollection services) =>
            services.AddDurableTaskClient(this.key, builder => builder.UseOrchestrationService(options =>
            {
                options.Client = this.service.Object;
                options.EnableEntitySupport = false;
                options.DataConverter = JsonDataConverter.Default;
            }));
    }
}
