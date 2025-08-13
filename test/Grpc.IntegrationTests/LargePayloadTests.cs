// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class LargePayloadTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) : IntegrationTestBase(output, sidecarFixture)
{
    [Fact]
    public async Task OrchestrationInput_IsExternalizedByClient_ResolvedByWorker()
    {
        string largeInput = new string('A', 1024 * 1024); // 1MB
        TaskName orchestratorName = nameof(OrchestrationInput_IsExternalizedByClient_ResolvedByWorker);

        InMemoryPayloadStore fakeStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string, string>(
                    orchestratorName,
                    (ctx, input) => Task.FromResult(input)));

                // Enable externalization on the worker
                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // small threshold to force externalization for test data
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });

                // Override store with in-memory test double
                worker.Services.AddSingleton<IPayloadStore>(fakeStore);
            },
            client =>
            {
                // Enable externalization on the client
                client.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });

                // Override store with in-memory test double
                client.Services.AddSingleton<IPayloadStore>(fakeStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: largeInput);

        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);

        // Validate that the input made a roundtrip and was resolved on the worker
        string? echoed = completed.ReadOutputAs<string>();
        Assert.NotNull(echoed);
        Assert.Equal(largeInput.Length, echoed!.Length);

        // Ensure client externalized the input
        Assert.True(fakeStore.UploadCount >= 1);
    }

    [Fact]
    public async Task ActivityInput_IsExternalizedByWorker_ResolvedByActivity()
    {
        string largeParam = new string('P', 700 * 1024); // 700KB
        TaskName orchestratorName = nameof(ActivityInput_IsExternalizedByWorker_ResolvedByActivity);
        TaskName activityName = "EchoLength";

        InMemoryPayloadStore workerStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, int>(
                        orchestratorName,
                        (ctx, _) => ctx.CallActivityAsync<int>(activityName, largeParam))
                    .AddActivityFunc<string, int>(activityName, (ctx, input) => input.Length));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // force externalization for activity input
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client => { /* client not needed for externalization path here */ });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(largeParam.Length, completed.ReadOutputAs<int>());

        // Worker externalizes when sending activity input; worker resolves when delivering to activity
        Assert.True(workerStore.UploadCount >= 1);
        Assert.True(workerStore.DownloadCount >= 1);
    }

    [Fact]
    public async Task ActivityOutput_IsExternalizedByWorker_ResolvedByOrchestrator()
    {
        string largeResult = new string('R', 850 * 1024); // 850KB
        TaskName orchestratorName = nameof(ActivityOutput_IsExternalizedByWorker_ResolvedByOrchestrator);
        TaskName activityName = "ProduceLarge";

        InMemoryPayloadStore workerStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, int>(
                        orchestratorName,
                        async (ctx, _) => (await ctx.CallActivityAsync<string>(activityName)).Length)
                    .AddActivityFunc<string>(activityName, (ctx) => Task.FromResult(largeResult)));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // force externalization for activity result
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client => { });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(largeResult.Length, completed.ReadOutputAs<int>());

        // Worker externalizes activity output and downloads when the orchestrator reads it
        Assert.True(workerStore.UploadCount >= 1);
        Assert.True(workerStore.DownloadCount >= 1);
    }

    [Fact]
    public async Task QueryCompletedInstance_DownloadsExternalizedOutputOnClient()
    {
        string largeOutput = new string('Q', 900 * 1024); // 900KB
        string smallInput = "input";
        TaskName orchestratorName = nameof(QueryCompletedInstance_DownloadsExternalizedOutputOnClient);

        Dictionary<string, string> shared = new System.Collections.Generic.Dictionary<string, string>();
        InMemoryPayloadStore workerStore = new InMemoryPayloadStore(shared);
        InMemoryPayloadStore clientStore = new InMemoryPayloadStore(shared);

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                    orchestratorName,
                    (ctx, _) => Task.FromResult(largeOutput)));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // force externalization on worker
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // allow client to resolve on query
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(clientStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: smallInput);
        await server.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: false, this.TimeoutToken);

        OrchestrationMetadata? queried = await server.Client.GetInstanceAsync(instanceId, getInputsAndOutputs: true);

        Assert.NotNull(queried);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, queried!.RuntimeStatus);
        Assert.Equal(smallInput, queried.ReadInputAs<string>());
        Assert.Equal(largeOutput, queried.ReadOutputAs<string>());

        Assert.True(workerStore.UploadCount == 0);
        Assert.True(clientStore.DownloadCount == 1);
        Assert.True(clientStore.UploadCount == 1);
    }

    [Fact]
    public async Task BelowThreshold_NotExternalized()
    {
        string smallPayload = new string('X', 64 * 1024); // 64KB
        TaskName orchestratorName = nameof(BelowThreshold_NotExternalized);

        InMemoryPayloadStore workerStore = new InMemoryPayloadStore();
        InMemoryPayloadStore clientStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string, string>(
                    orchestratorName,
                    (ctx, input) => Task.FromResult(input)));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 2 * 1024 * 1024; // 2MB, higher than payload
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 2 * 1024 * 1024; // 2MB, higher than payload
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(clientStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: smallPayload);
        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(smallPayload, completed.ReadOutputAs<string>());

        Assert.Equal(0, workerStore.UploadCount);
        Assert.Equal(0, workerStore.DownloadCount);
        Assert.Equal(0, clientStore.UploadCount);
        Assert.Equal(0, clientStore.DownloadCount);
    }

    [Fact]
    public async Task ExternalEventPayload_IsExternalizedByClient_ResolvedByWorker()
    {
        string largeEvent = new string('E', 512 * 1024); // 512KB
        TaskName orchestratorName = nameof(ExternalEventPayload_IsExternalizedByClient_ResolvedByWorker);
        const string EventName = "LargeEvent";

        InMemoryPayloadStore fakeStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string>(
                    orchestratorName,
                    async ctx => await ctx.WaitForExternalEvent<string>(EventName)));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // force externalization
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(fakeStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // force externalization
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(fakeStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        await server.Client.RaiseEventAsync(instanceId, EventName, largeEvent, this.TimeoutToken);

        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        string? output = completed.ReadOutputAs<string>();
        Assert.Equal(largeEvent, output);
        Assert.True(fakeStore.UploadCount >= 1);
    }

    [Fact]
    public async Task OutputAndCustomStatus_ExternalizedByWorker_ResolvedOnQuery()
    {
        string largeOutput = new string('O', 768 * 1024); // 768KB
        string largeStatus = new string('S', 600 * 1024); // 600KB
        TaskName orchestratorName = nameof(OutputAndCustomStatus_ExternalizedByWorker_ResolvedOnQuery);

        InMemoryPayloadStore fakeStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                    orchestratorName,
                    async (ctx, _) =>
                    {
                        ctx.SetCustomStatus(largeStatus);
                        await ctx.CreateTimer(TimeSpan.Zero, CancellationToken.None);
                        return largeOutput;
                    }));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // ensure externalization for status/output
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(fakeStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.Enabled = true;
                    opts.ExternalizeThresholdBytes = 1024; // ensure resolution on query
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(fakeStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);

        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(largeOutput, completed.ReadOutputAs<string>());
        Assert.Equal(largeStatus, completed.ReadCustomStatusAs<string>());

        // Worker may externalize both status and output
        Assert.True(fakeStore.UploadCount >= 2);
    }

    class InMemoryPayloadStore : IPayloadStore
    {
        readonly Dictionary<string, string> tokenToPayload;

        public InMemoryPayloadStore()
            : this(new Dictionary<string, string>())
        {
        }

        public InMemoryPayloadStore(Dictionary<string, string> shared)
        {
            this.tokenToPayload = shared;
        }

        int uploadCount;
        public int UploadCount => this.uploadCount;
        int downloadCount;
        public int DownloadCount => this.downloadCount;

        public Task<string> UploadAsync(string contentType, ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.uploadCount);
            string json = System.Text.Encoding.UTF8.GetString(payloadBytes.Span);
            string token = $"dts:v1:test:{Guid.NewGuid():N}";
            this.tokenToPayload[token] = json;
            return Task.FromResult(token);
        }

        public Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.downloadCount);
            return Task.FromResult(this.tokenToPayload[token]);
        }
    }
}
