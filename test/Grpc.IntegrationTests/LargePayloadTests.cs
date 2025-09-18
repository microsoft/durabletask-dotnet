// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class LargePayloadTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) : IntegrationTestBase(output, sidecarFixture)
{
    // Validates client externalizes a large orchestration input and worker resolves it.
    [Fact]
    public async Task LargeOrchestrationInputAndOutputAndCustomStatus()
    {
        string largeInput = new string('A', 1024 * 1024); // 1MB
        TaskName orchestratorName = nameof(LargeOrchestrationInputAndOutputAndCustomStatus);

        InMemoryPayloadStore fakeStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string, string>(
                    orchestratorName,
                    (ctx, input) =>
                    {
                        ctx.SetCustomStatus(largeInput);
                        return Task.FromResult(input + input);
                    }));

                // Enable externalization on the worker
                worker.UseExternalizedPayloads(opts =>
                {
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
        // validate input
        string? input = completed.ReadInputAs<string>();
        Assert.NotNull(input);
        Assert.Equal(largeInput.Length, input!.Length);
        Assert.Equal(largeInput, input);

        string? echoed = completed.ReadOutputAs<string>();
        Assert.NotNull(echoed);
        Assert.Equal(largeInput.Length * 2, echoed!.Length);
        Assert.Equal(largeInput + largeInput, echoed);

        string? customStatus = completed.ReadCustomStatusAs<string>();
        Assert.NotNull(customStatus);
        Assert.Equal(largeInput.Length, customStatus!.Length);
        Assert.Equal(largeInput, customStatus);

        // Ensure client externalized the input
        Assert.True(fakeStore.UploadCount >= 1);
        Assert.True(fakeStore.DownloadCount >= 1);
        Assert.Contains(JsonSerializer.Serialize(largeInput), fakeStore.uploadedPayloads);
        Assert.Contains(JsonSerializer.Serialize(largeInput + largeInput), fakeStore.uploadedPayloads);
    }

    // Validates history streaming path resolves externalized inputs/outputs in HistoryChunk.
    [Fact]
    public async Task HistoryStreaming_ResolvesPayloads()
    {
        // Make payloads large enough so that past events history exceeds 1 MiB to trigger streaming
        string largeInput = new string('H', 2 * 1024 * 1024);   // 2 MiB
        string largeOutput = new string('O', 2 * 1024 * 1024);  // 2 MiB
        TaskName orch = nameof(HistoryStreaming_ResolvesPayloads);

        InMemoryPayloadStore store = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string, string>(
                    orch,
                    async (ctx, input) =>
                    {
                        // Emit several events so that the serialized history size grows
                        for (int i = 0; i < 50; i++)
                        {
                            await ctx.CreateTimer(TimeSpan.FromMilliseconds(10), CancellationToken.None);
                        }
                        return largeOutput;
                    }));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(store);
            },
            client =>
            {
                // Enable client to resolve outputs on query
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(store);
            });

        // Start orchestration with large input to exercise history input resolution
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch, largeInput);
        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(largeInput, completed.ReadInputAs<string>());
        Assert.Equal(largeOutput, completed.ReadOutputAs<string>());
        Assert.True(store.UploadCount >= 2);
        Assert.True(store.DownloadCount >= 2);
    }

    // Validates client externalizes large suspend and resume reasons.
    [Fact]
    public async Task SuspendAndResume_Reason_IsExternalizedByClient()
    {
        string largeReason1 = new string('Z', 700 * 1024); // 700KB
        string largeReason2 = new string('Y', 650 * 1024); // 650KB
        TaskName orchestratorName = nameof(SuspendAndResume_Reason_IsExternalizedByClient);

        InMemoryPayloadStore clientStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                // Long-running orchestrator to give time for suspend/resume
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                    orchestratorName,
                    async (ctx, _) =>
                    {
                        await ctx.CreateTimer(TimeSpan.FromMinutes(5), CancellationToken.None);
                        return "done";
                    }));
            },
            client =>
            {
                // Enable externalization on the client and use the in-memory store to track uploads
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024; // 1KB threshold to force externalization
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(clientStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        // Suspend with large reason (should be externalized by client)
        await server.Client.SuspendInstanceAsync(instanceId, largeReason1, this.TimeoutToken);
        await server.Client.WaitForInstanceStartAsync(instanceId, this.TimeoutToken);

        // poll up to 5 seconds to verify it is suspended
        var deadline1 = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            OrchestrationMetadata? status1 = await server.Client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, this.TimeoutToken);
            if (status1 is not null && status1.RuntimeStatus == OrchestrationRuntimeStatus.Suspended)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline1)
            {
                Assert.NotNull(status1);
                Assert.Equal(OrchestrationRuntimeStatus.Suspended, status1!.RuntimeStatus);
            }
        }
        // Resume with large reason (should be externalized by client)
        await server.Client.ResumeInstanceAsync(instanceId, largeReason2, this.TimeoutToken);

        // verify it is resumed (poll up to 5 seconds)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            OrchestrationMetadata? status = await server.Client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, this.TimeoutToken);
            if (status is not null && status.RuntimeStatus == OrchestrationRuntimeStatus.Running)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                Assert.NotNull(status);
                Assert.Equal(OrchestrationRuntimeStatus.Running, status!.RuntimeStatus);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), this.TimeoutToken);
        }



        Assert.True(clientStore.UploadCount >= 2);
        Assert.Contains(largeReason1, clientStore.uploadedPayloads);
        Assert.Contains(largeReason2, clientStore.uploadedPayloads);
    }

    // Validates terminating an instance with a large output payload is externalized by the client.
    [Fact]
    public async Task LargeTerminateWithPayload()
    {
        string largeInput = new string('I', 900 * 1024);
        string largeOutput = new string('T', 900 * 1024);
        TaskName orch = nameof(LargeTerminateWithPayload);

        InMemoryPayloadStore store = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, object?>(
                    orch,
                    async (ctx, _) =>
                    {
                        await ctx.CreateTimer(TimeSpan.FromSeconds(30), CancellationToken.None);
                        return null;
                    }));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(store);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(store);
            });

        string id = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch, largeInput);
        await server.Client.WaitForInstanceStartAsync(id, this.TimeoutToken);

        await server.Client.TerminateInstanceAsync(id, new TerminateInstanceOptions { Output = largeOutput }, this.TimeoutToken);

        await server.Client.WaitForInstanceCompletionAsync(id, this.TimeoutToken);
        OrchestrationMetadata? status = await server.Client.GetInstanceAsync(id, getInputsAndOutputs: false);
        Assert.NotNull(status);
        Assert.Equal(OrchestrationRuntimeStatus.Terminated, status!.RuntimeStatus);
        Assert.True(store.UploadCount >= 1);
        Assert.True(store.DownloadCount >= 1);
        Assert.Contains(JsonSerializer.Serialize(largeOutput), store.uploadedPayloads);
    }
    // Validates large custom status and ContinueAsNew input are externalized and resolved across iterations.
    [Fact]
    public async Task LargeContinueAsNewAndCustomStatus()
    {
        string largeStatus = new string('S', 700 * 1024);
        string largeNextInput = new string('N', 800 * 1024);
        string largeFinalOutput = new string('F', 750 * 1024);
        TaskName orch = nameof(LargeContinueAsNewAndCustomStatus);

        var shared = new Dictionary<string, string>();
        InMemoryPayloadStore workerStore = new InMemoryPayloadStore(shared);

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string?, string>(
                    orch,
                    async (ctx, input) =>
                    {
                        if (input == null)
                        {
                            ctx.SetCustomStatus(largeStatus);
                            ctx.ContinueAsNew(largeNextInput);
                            // unreachable
                            return "";
                        }
                        else
                        {
                            // second iteration returns final
                            return largeFinalOutput;
                        }
                    }));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(workerStore);
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch);
        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        Assert.Equal(largeFinalOutput, completed.ReadOutputAs<string>());
        Assert.Contains(JsonSerializer.Serialize(largeStatus), workerStore.uploadedPayloads);
        Assert.Contains(JsonSerializer.Serialize(largeNextInput), workerStore.uploadedPayloads);
        Assert.Contains(JsonSerializer.Serialize(largeFinalOutput), workerStore.uploadedPayloads);
    }

    // Validates large sub-orchestration input and an activity large output in one flow.
    [Fact]
    public async Task LargeSubOrchestrationAndActivityOutput()
    {
        string largeChildInput = new string('C', 650 * 1024);
        string largeActivityOutput = new string('A', 820 * 1024);
        TaskName parent = nameof(LargeSubOrchestrationAndActivityOutput) + "_Parent";
        TaskName child = nameof(LargeSubOrchestrationAndActivityOutput) + "_Child";
        TaskName activity = "ProduceBig";

        var shared = new Dictionary<string, string>();
        InMemoryPayloadStore workerStore = new InMemoryPayloadStore(shared);

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, int>(
                        parent,
                        async (ctx, _) =>
                        {
                            string echoed = await ctx.CallSubOrchestratorAsync<string>(child, largeChildInput);
                            string act = await ctx.CallActivityAsync<string>(activity);
                            return echoed.Length + act.Length;
                        })
                    .AddOrchestratorFunc<string, string>(child, (ctx, input) => Task.FromResult(input))
                    .AddActivityFunc<string>(activity, (ctx) => Task.FromResult(largeActivityOutput)));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(workerStore);
            });

        string id = await server.Client.ScheduleNewOrchestrationInstanceAsync(parent);
        OrchestrationMetadata done = await server.Client.WaitForInstanceCompletionAsync(
            id, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        Assert.Equal(largeChildInput.Length + largeActivityOutput.Length, done.ReadOutputAs<int>());
        Assert.True(workerStore.UploadCount >= 1);
        Assert.True(workerStore.DownloadCount >= 1);
        Assert.Contains(JsonSerializer.Serialize(largeChildInput), workerStore.uploadedPayloads);
        Assert.Contains(JsonSerializer.Serialize(largeActivityOutput), workerStore.uploadedPayloads);
    }

    // Validates query with fetch I/O resolves large outputs for completed instances.
    [Fact]
    public async Task LargeQueryFetchInputsAndOutputs()
    {
        string largeIn = new string('I', 750 * 1024);
        string largeOut = new string('Q', 880 * 1024);
        TaskName orch = nameof(LargeQueryFetchInputsAndOutputs);

        var shared = new Dictionary<string, string>();
        InMemoryPayloadStore workerStore = new InMemoryPayloadStore(shared);

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                    orch,
                    (ctx, input) => Task.FromResult(largeOut)));

                worker.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                worker.Services.AddSingleton<IPayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
                client.Services.AddSingleton<IPayloadStore>(workerStore);
            });

        string id = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch, largeIn);
        await server.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: false, this.TimeoutToken);

        var page = server.Client.GetAllInstancesAsync(new OrchestrationQuery { FetchInputsAndOutputs = true, InstanceIdPrefix = id });
        OrchestrationMetadata? found = null;
        await foreach (var item in page)
        {
            if (item.Name == orch.Name)
            {
                found = item;
                break;
            }
        }

        Assert.NotNull(found);
        Assert.Equal(largeOut, found!.ReadOutputAs<string>());
        Assert.True(workerStore.DownloadCount >= 1);
        Assert.True(workerStore.UploadCount >= 1);
        Assert.Contains(JsonSerializer.Serialize(largeIn), workerStore.uploadedPayloads);
        Assert.Contains(JsonSerializer.Serialize(largeOut), workerStore.uploadedPayloads);
    }
    // Validates worker externalizes large activity input and delivers resolved payload to activity.
    [Fact]
    public async Task LargeActivityInputAndOutput()
    {
        string largeParam = new string('P', 700 * 1024); // 700KB
        TaskName orchestratorName = nameof(LargeActivityInputAndOutput);
        TaskName activityName = "EchoLength";

        InMemoryPayloadStore workerStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, string>(
                        orchestratorName,
                        (ctx, _) => ctx.CallActivityAsync<string>(activityName, largeParam))
                    .AddActivityFunc<string, string>(activityName, (ctx, input) => input + input));

                worker.UseExternalizedPayloads(opts =>
                {
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

        // validate upload and download count
        Assert.True(workerStore.UploadCount >= 1);
        Assert.True(workerStore.DownloadCount >= 1);

        // validate uploaded payloads include the activity input and output forms
        string expectedActivityInputJson = JsonSerializer.Serialize(new[] { largeParam });
        string expectedActivityOutputJson = JsonSerializer.Serialize(largeParam + largeParam);
        Assert.Contains(expectedActivityInputJson, workerStore.uploadedPayloads);
        Assert.Contains(expectedActivityOutputJson, workerStore.uploadedPayloads);
    }


    // Ensures payloads below the threshold are not externalized by client or worker.
    [Fact]
    public async Task NoLargePayloads()
    {
        string smallPayload = new string('X', 64 * 1024); // 64KB
        TaskName orchestratorName = nameof(NoLargePayloads);

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

    // Validates client externalizes a large external event payload and worker resolves it.
    [Fact]
    public async Task LargeExternalEvent()
    {
        string largeEvent = new string('E', 512 * 1024); // 512KB
        TaskName orchestratorName = nameof(LargeExternalEvent);
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
        Assert.True(fakeStore.DownloadCount >= 1);
        Assert.Contains(JsonSerializer.Serialize(largeEvent), fakeStore.uploadedPayloads);
    }


    class InMemoryPayloadStore : IPayloadStore
    {
        const string TokenPrefix = "blob:v1:";
        readonly Dictionary<string, string> tokenToPayload;
        public readonly HashSet<string> uploadedPayloads = new();

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

        public Task<string> UploadAsync(ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.uploadCount);
            string json = System.Text.Encoding.UTF8.GetString(payloadBytes.Span);
            string token = $"blob:v1:test:{Guid.NewGuid():N}";
            this.tokenToPayload[token] = json;
            this.uploadedPayloads.Add(json);
            return Task.FromResult(token);
        }

        public Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.downloadCount);
            return Task.FromResult(this.tokenToPayload[token]);
        }

        public bool IsKnownPayloadToken(string value)
        {
            return value.StartsWith(TokenPrefix, StringComparison.Ordinal);
        }

    }
}
