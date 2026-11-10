// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using Azure;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Tests;

public class LargePayloadTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture) : IntegrationTestBase(output, sidecarFixture)
{
    [Theory]
    [InlineData(0, "activity")]
    [InlineData(403, "activity")]
    [InlineData(404, "activity")]
    [InlineData(0, "orchestration")]
    [InlineData(403, "orchestration")]
    [InlineData(404, "orchestration")]
    [InlineData(404, "suborchestration")]
    public async Task HydrationFailure_KeepsHealthyActivitiesOnSameStreamAsync(int storageStatus, string target)
    {
        // Arrange
        const string Token = "blob:v1:missing";
        TaskName orchestration = nameof(HydrationFailure_KeepsHealthyActivitiesOnSameStreamAsync);
        TaskName activity = "HydrationActivity";
        TaskName child = "HydrationChild";
        TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource downloadFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<PayloadStore> store = new();
        store.Setup(s => s.IsKnownPayloadToken(It.IsAny<string>())).Returns((string value) => value == Token);
        store.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Token);
        store.Setup(s => s.DownloadAsync(Token, It.IsAny<CancellationToken>())).Returns(() =>
        {
            downloadFailed.TrySetResult();
            Exception failure = storageStatus == 0
                ? new IOException("Download failed.") : new RequestFailedException(storageStatus, "Download failed.");
            return Task.FromException<string>(failure);
        });
        await using HostTestLifetime server = await this.StartWorkerAsync(worker =>
        {
            worker.AddTasks(tasks => tasks
                .AddOrchestratorFunc<string, string>(orchestration, (context, input) =>
                    input == "bad-child"
                        ? context.CallSubOrchestratorAsync<string>(child, new string('X', 1024))
                        : context.CallActivityAsync<string>(activity, input == "bad" ? new string('X', 1024) : input))
                .AddOrchestratorFunc<string, string>(child, (_, input) => Task.FromResult(input))
                .AddActivityFunc<string, string>(activity, async (_, input) =>
                {
                    if (input == "running")
                    {
                        running.TrySetResult();
                        await release.Task.WaitAsync(this.TimeoutToken);
                    }

                    return input;
                }));
            worker.UseExternalizedPayloads();
            worker.Services.AddSingleton(store.Object);
        }, clientConfigure: client => client.UseExternalizedPayloads(),
        servicesConfigure: services => services.AddExternalizedPayloadStore(options =>
        {
            options.ThresholdBytes = 16;
            options.ConnectionString = "UseDevelopmentStorage=true";
        }));

        try
        {
            // Act
            string alreadyRunning = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestration, "running");
            await running.Task.WaitAsync(this.TimeoutToken);
            string failed = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestration, target switch
            {
                "orchestration" => new string('X', 1024),
                "suborchestration" => "bad-child",
                _ => "bad",
            });
            await downloadFailed.Task.WaitAsync(this.TimeoutToken);
            string following = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestration, "following");
            OrchestrationMetadata followingResult = await server.Client.WaitForInstanceCompletionAsync(
                following, getInputsAndOutputs: true, this.TimeoutToken);
            release.TrySetResult();
            OrchestrationMetadata runningResult = await server.Client.WaitForInstanceCompletionAsync(
                alreadyRunning, getInputsAndOutputs: true, this.TimeoutToken);

            // Assert
            Assert.Equal(OrchestrationRuntimeStatus.Completed, followingResult.RuntimeStatus);
            Assert.Equal("following", followingResult.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, runningResult.RuntimeStatus);
            Assert.Equal("running", runningResult.ReadOutputAs<string>());
            Assert.Single(this.GetLogs(), log => log.EventId.Name == "EstablishedWorkItemConnection");
            if (storageStatus == 404)
            {
                OrchestrationMetadata failure = await server.Client.WaitForInstanceCompletionAsync(
                    failed, getInputsAndOutputs: false, this.TimeoutToken);
                Assert.Equal(OrchestrationRuntimeStatus.Failed, failure.RuntimeStatus);
            }
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletionRetry_DoesNotExternalizePayloadTokensAgain(bool retryActivity)
    {
        // Arrange
        string payload = new('R', 1024);
        TaskName orchestratorName = nameof(CompletionRetry_DoesNotExternalizePayloadTokensAgain);
        TaskName activityName = "RetryPayloadActivity";
        InMemoryPayloadStore store = new();
        RetryCompletionInterceptor retry = new(retryActivity);
        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, string>(orchestratorName, async (context, _) =>
                    {
                        string result = await context.CallActivityAsync<string>(activityName);
                        context.SetCustomStatus(result);
                        return result;
                    })
                    .AddActivityFunc<string>(activityName, _ => Task.FromResult(payload)));
                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(store);
                worker.Services.PostConfigure<GrpcDurableTaskWorkerOptions>(
                    options => options.Interceptors.Add(retry));
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(store);
            },
            services => services.AddExternalizedPayloadStore(options =>
            {
                options.ThresholdBytes = 16;
                options.ConnectionString = "UseDevelopmentStorage=true";
            }));

        // Act
        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata result = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        // Assert
        retry.Payloads.Should().HaveCount(2);
        retry.Payloads[0].Length.Should().BeGreaterThan(16);
        retry.Payloads[1].Should().Be(retry.Payloads[0]);
        store.UploadCount.Should().Be(3, "only the activity result, orchestration result, and custom status need uploading");
        store.uploadedPayloads.Should().NotContain(value => store.IsKnownPayloadToken(value));
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.ReadOutputAs<string>().Should().Be(payload);
        result.ReadCustomStatusAs<string>().Should().Be(payload);
    }

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

                worker.UseExternalizedPayloads();

                worker.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();

                // Override store with in-memory test double
                client.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(store);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(store);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(clientStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(store);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(store);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(workerStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(workerStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(workerStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(workerStore);
            },
            client => { /* client not needed for externalization path here */ },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
            });

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

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(workerStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(clientStore);
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

    // Validates that payloads exceeding max cap are rejected with a clear error.
    [Fact]
    public async Task ExceedingMaxPayloadIsRejected()
    {
        string tooLarge = new string('Z', 3044);
        TaskName orch = nameof(ExceedingMaxPayloadIsRejected);

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string?, string>(
                    orch,
                    (ctx, input) => Task.FromResult("done")));
                worker.UseExternalizedPayloads();
            },
            client =>
            {
                client.UseExternalizedPayloads();
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    // Keep a low threshold to force externalization, but default cap applies
                    opts.ThresholdBytes = 1024;
                    opts.MaxPayloadBytes = 2048;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
            });

        // The client will attempt to externalize the input and should fail fast on cap
        await Assert.ThrowsAsync<PayloadStorageException>(async () =>
        {
            await server.Client.ScheduleNewOrchestrationInstanceAsync(orch, tooLarge);
        });
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

                worker.Services.AddSingleton<PayloadStore>(fakeStore);
                worker.UseExternalizedPayloads();
            },
            client =>
            {
                client.Services.AddSingleton<PayloadStore>(fakeStore);
                client.UseExternalizedPayloads();
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
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


    // Validates that when an activity output exceeds MaxPayloadBytes, the orchestration
    // fails gracefully instead of getting stuck in an infinite retry loop.
    [Fact]
    public async Task ActivityOutputExceedsMaxPayload_OrchestrationFailsGracefully()
    {
        // Activity returns output larger than MaxPayloadBytes cap
        string oversizedOutput = new string('X', 3000);
        TaskName orch = nameof(ActivityOutputExceedsMaxPayload_OrchestrationFailsGracefully);
        TaskName activity = "ProduceOversized";

        InMemoryPayloadStore fakeStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, string>(
                        orch,
                        async (ctx, _) => await ctx.CallActivityAsync<string>(activity))
                    .AddActivityFunc<string>(activity, (ctx) => Task.FromResult(oversizedOutput)));

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.MaxPayloadBytes = 2048;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch);
        OrchestrationMetadata result = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        // The orchestration should fail (not hang forever) because the activity's oversized
        // output causes payload externalization to throw (e.g. PayloadStorageException or
        // RequestFailedException), which is caught and converted into a non-retriable FailureDetails.
        Assert.Equal(OrchestrationRuntimeStatus.Failed, result.RuntimeStatus);
        Assert.NotNull(result.FailureDetails);
        Assert.Contains("exceeds the configured maximum", result.FailureDetails!.ErrorMessage);
    }

    // Validates that when an orchestration's own output exceeds MaxPayloadBytes,
    // it fails gracefully instead of getting stuck in an infinite retry loop.
    [Fact]
    public async Task OrchestrationOutputExceedsMaxPayload_FailsGracefully()
    {
        // Orchestration returns output larger than MaxPayloadBytes cap
        string oversizedOutput = new string('Y', 3000);
        TaskName orch = nameof(OrchestrationOutputExceedsMaxPayload_FailsGracefully);

        InMemoryPayloadStore fakeStore = new InMemoryPayloadStore();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<object?, string>(
                    orch,
                    (ctx, _) => Task.FromResult(oversizedOutput)));

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.MaxPayloadBytes = 2048;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch);
        OrchestrationMetadata result = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        // The orchestration should fail (not hang forever) because the oversized output
        // triggers a PayloadStorageException in the interceptor, which replaces the response
        // with a non-retriable failure completion directly (no exception escapes to the Processor).
        Assert.Equal(OrchestrationRuntimeStatus.Failed, result.RuntimeStatus);
        Assert.NotNull(result.FailureDetails);
        Assert.Contains("exceeds the configured maximum", result.FailureDetails!.ErrorMessage);
    }

    [Theory]
    [InlineData(1, 5 * 1024 * 1024)]
    [InlineData(5, 1024 * 1024)]
    public async Task OversizedInlineResponse_FailsWithoutSchedulingActivities(int activityCount, int payloadBytes)
    {
        TaskName orchestratorName = nameof(OversizedInlineResponse_FailsWithoutSchedulingActivities);
        TaskName activityName = "MustNotRun";
        int activitiesRun = 0;
        await using HostTestLifetime server = await this.StartWorkerAsync(worker =>
            worker.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, async context =>
                {
                    context.SetCustomStatus("pending");
                    Task<int>[] activities = [.. Enumerable.Range(0, activityCount).Select(_ =>
                        context.CallActivityAsync<int>(activityName, new string('A', payloadBytes)))];
                    return (await Task.WhenAll(activities)).Sum();
                })
                .AddActivityFunc<string, int>(activityName, (_, input) =>
                {
                    Interlocked.Increment(ref activitiesRun);
                    return Task.FromResult(input.Length);
                })));

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(this.TimeoutToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        OrchestrationMetadata result = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, timeout.Token);

        Assert.Equal(OrchestrationRuntimeStatus.Failed, result.RuntimeStatus);
        Assert.NotNull(result.FailureDetails);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.FailureDetails.ErrorType);
        Assert.Contains("inline limit", result.FailureDetails.ErrorMessage);
        Assert.Null(result.ReadCustomStatusAs<string>());
        Assert.Equal(0, Volatile.Read(ref activitiesRun));
    }

    [Fact]
    public async Task LegacyChunkSize_DoesNotLimitInlineResponses()
    {
        const int PayloadBytes = 2 * 1024 * 1024;
        TaskName orchestratorName = nameof(LegacyChunkSize_DoesNotLimitInlineResponses);
        TaskName activityName = "GetInputLength";
        await using HostTestLifetime server = await this.StartWorkerAsync(worker =>
        {
#pragma warning disable CS0618 // Check that the old setting has no effect.
            worker.UseGrpc(options => options.CompleteOrchestrationWorkItemChunkSizeInBytes = 1024 * 1024);
#pragma warning restore CS0618
            worker.AddTasks(tasks => tasks
                .AddOrchestratorFunc(orchestratorName, context =>
                    context.CallActivityAsync<int>(activityName, new string('A', PayloadBytes)))
                .AddActivityFunc<string, int>(activityName, (_, input) => Task.FromResult(input.Length)));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orchestratorName);
        OrchestrationMetadata result = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        Assert.Equal(PayloadBytes, result.ReadOutputAs<int>());
    }

    // Externalization lets a 5 MiB result pass through the worker and client.
    [Fact]
    public async Task LargeActivityOutput_ExceedsGrpcLimit_ExternalizesSuccessfully()
    {
        string largeOutput = new('L', 5 * 1024 * 1024);
        TaskName orch = nameof(LargeActivityOutput_ExceedsGrpcLimit_ExternalizesSuccessfully);
        TaskName activity = "ProduceLarge5MB";

        InMemoryPayloadStore fakeStore = new();

        await using HostTestLifetime server = await this.StartWorkerAsync(
            worker =>
            {
                worker.AddTasks(tasks => tasks
                    .AddOrchestratorFunc<object?, string>(
                        orch,
                        async (ctx, _) => await ctx.CallActivityAsync<string>(activity))
                    .AddActivityFunc<string>(activity, (ctx) => Task.FromResult(largeOutput)));

                worker.UseExternalizedPayloads();
                worker.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            client =>
            {
                client.UseExternalizedPayloads();
                client.Services.AddSingleton<PayloadStore>(fakeStore);
            },
            services =>
            {
                services.AddExternalizedPayloadStore(opts =>
                {
                    opts.ThresholdBytes = 1024;
                    opts.ContainerName = "test";
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                });
            });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(orch);
        OrchestrationMetadata completed = await server.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, this.TimeoutToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, completed.RuntimeStatus);
        string? output = completed.ReadOutputAs<string>();
        Assert.Equal(largeOutput, output);
    }

    sealed class RetryCompletionInterceptor(bool retryActivity) : Interceptor
    {
        public List<string> Payloads { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            string? payload = request switch
            {
                P.ActivityResponse response when retryActivity => response.Result,
                P.OrchestratorResponse response when !retryActivity =>
                    response.Actions.FirstOrDefault(action => action.CompleteOrchestration is not null)
                        ?.CompleteOrchestration.Result,
                _ => null,
            };
            if (payload is not null)
            {
                this.Payloads.Add(payload);
                if (this.Payloads.Count == 1)
                {
                    Status status = new(StatusCode.Unavailable, "Simulated completion transport failure.");
                    return new AsyncUnaryCall<TResponse>(
                        Task.FromException<TResponse>(new RpcException(status)),
                        Task.FromResult(new Metadata()),
                        () => status,
                        () => new Metadata(),
                        () => { });
                }
            }

            return continuation(request, context);
        }
    }

    class InMemoryPayloadStore : PayloadStore
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

        public override Task<string> UploadAsync(string payLoad, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.uploadCount);
            string token = $"blob:v1:test:{Guid.NewGuid():N}";
            this.tokenToPayload[token] = payLoad;
            this.uploadedPayloads.Add(payLoad);
            return Task.FromResult(token);
        }

        public override Task<string> DownloadAsync(string token, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.downloadCount);
            return Task.FromResult(this.tokenToPayload[token]);
        }

        public override bool IsKnownPayloadToken(string value)
        {
            return value.StartsWith(TokenPrefix, StringComparison.Ordinal);
        }
    }
}
