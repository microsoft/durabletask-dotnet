// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using P = Microsoft.DurableTask.Protobuf;
using LP = Microsoft.DurableTask.Protobuf.LargePayloads;
using Match = Microsoft.DurableTask.Worker.DurableTaskWorkerOptions.VersionMatchStrategy;
using Failure = Microsoft.DurableTask.Worker.DurableTaskWorkerOptions.VersionFailureStrategy;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.AutoPurge;

/// <summary>
/// Delivers protobuf work items through a real worker, registry, and execution shims; only RPC replies are simulated.
/// </summary>
public class PurgeVersioningTests(ITestOutputHelper output)
{
    static readonly string[] InternalActivities =
    [
        nameof(GetLargePayloadTombstonesActivity),
        nameof(DeleteExternalBlobActivity),
        nameof(ReportLargePayloadPurgeResultsActivity),
    ];

    [Theory]
    [InlineData(Match.Strict, Failure.Reject, "", false)]
    [InlineData(Match.Strict, Failure.Fail, "3.0", false)]
    [InlineData(Match.CurrentOrOlder, Failure.Reject, "3.0", false)]
    [InlineData(Match.Strict, Failure.Reject, "3.0", true)]
    [InlineData(Match.None, Failure.Reject, "3.0", false)]
    public async Task InternalTasks_ExecuteWithoutBusinessVersionRestrictionsAsync(
        Match strategy, Failure failure, string version, bool lowerCase)
    {
        // Arrange
        await using Fixture fixture = new(strategy, failure);
        await fixture.StartAsync();
        string name = lowerCase ? nameof(BlobPurgeJobOrchestrator).ToLowerInvariant() : nameof(BlobPurgeJobOrchestrator);

        // Act
        object? orchestrationResult = await fixture.ProcessAsync(Orchestration(name, version, new BlobPurgeJobRunRequest(23)));
        List<object?> activityResults = [];
        foreach (string activity in InternalActivities)
        {
            activityResults.Add(await fixture.ProcessAsync(Activity(lowerCase ? activity.ToLowerInvariant() : activity, version)));
        }

        // Assert
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            strategy, failure, version, lowerCase,
            Orchestration = orchestrationResult?.GetType().Name,
            Activities = activityResults.Select(result => result?.GetType().Name).ToArray(),
            fixture.Invoker.FetchCount, fixture.Invoker.ReportCount,
        }));
        P.OrchestratorResponse response = Assert.IsType<P.OrchestratorResponse>(orchestrationResult);
        P.ScheduleTaskAction scheduled = Assert.Single(response.Actions).ScheduleTask;
        Assert.NotNull(scheduled);
        Assert.Equal(nameof(GetLargePayloadTombstonesActivity), scheduled.Name);
        Assert.Equal(version, scheduled.Version ?? string.Empty);
        Assert.Equal("[23]", scheduled.Input);
        Assert.All(activityResults, result => Assert.Null(Assert.IsType<P.ActivityResponse>(result).FailureDetails));
        Assert.Equal(1, fixture.Invoker.FetchCount);
        Assert.Equal(1, fixture.Invoker.ReportCount);
        fixture.Store.Verify(store => store.DeleteAsync("blob:v2:https://account.invalid/payloads/blob", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(strategy, fixture.Versioning.MatchStrategy);
        Assert.Equal(failure, fixture.Versioning.FailureStrategy);
        Assert.Equal("2.0", fixture.Versioning.Version);
    }

    [Theory]
    [InlineData(Match.Strict, Failure.Reject, "3.0", false)]
    [InlineData(Match.Strict, Failure.Fail, "3.0", false)]
    [InlineData(Match.Strict, Failure.Reject, "2.0", true)]
    [InlineData(Match.CurrentOrOlder, Failure.Reject, "3.0", false)]
    [InlineData(Match.CurrentOrOlder, Failure.Fail, "3.0", false)]
    [InlineData(Match.CurrentOrOlder, Failure.Reject, "1.0", true)]
    [InlineData(Match.None, Failure.Fail, "3.0", true)]
    public async Task BusinessTasks_KeepTheirConfiguredVersionBehaviorAsync(
        Match strategy, Failure failure, string version, bool accepted)
    {
        // Arrange
        await using Fixture fixture = new(strategy, failure);
        await fixture.StartAsync();

        // Act
        object? orchestrationResult = await fixture.ProcessAsync(Orchestration("Business", version, "input"));
        object? activityResult = await fixture.ProcessAsync(Activity("Business", version));

        // Assert
        if (accepted)
        {
            Assert.Equal(P.OrchestrationStatus.Completed,
                Assert.Single(Assert.IsType<P.OrchestratorResponse>(orchestrationResult).Actions).CompleteOrchestration.OrchestrationStatus);
            Assert.Null(Assert.IsType<P.ActivityResponse>(activityResult).FailureDetails);
            Assert.Equal(2, fixture.BusinessCalls);
        }
        else
        {
            AssertRejected(orchestrationResult, activityResult, failure);
            Assert.Equal(0, fixture.BusinessCalls);
        }
    }

    [Theory]
    [InlineData("BlobPurgeJobOrchestrator", false)]
    [InlineData("GetLargePayloadTombstonesActivity", true)]
    [InlineData("DeleteExternalBlobActivity", true)]
    [InlineData("ReportLargePayloadPurgeResultsActivity", true)]
    [InlineData("BlobPurgeJobOrchestratorCustomer", true)]
    [InlineData("DeleteExternalBlobActivityCustomer", false)]
    public async Task Exemptions_DoNotMatchAnotherTaskKindOrNamePrefixAsync(string name, bool orchestration)
    {
        // Arrange
        await using Fixture fixture = new(Match.Strict, Failure.Reject);
        await fixture.StartAsync();

        // Act
        object? result = await fixture.ProcessAsync(
            orchestration ? Orchestration(name, "3.0", "input") : Activity(name, "3.0"));

        // Assert
        if (orchestration)
        {
            Assert.IsType<P.AbandonOrchestrationTaskRequest>(result);
        }
        else
        {
            Assert.IsType<P.AbandonActivityTaskRequest>(result);
        }

        Assert.Equal(0, fixture.BusinessCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerWithoutExtension_HasNoExemptionsEvenBesideEnabledWorkerAsync(bool withAnotherWorker)
    {
        // Arrange
        await using Fixture fixture = new(Match.Strict, Failure.Reject, enabled: false, withAnotherWorker: withAnotherWorker);
        await fixture.StartAsync();

        // Act
        object? orchestrationResult = await fixture.ProcessAsync(
            Orchestration(nameof(BlobPurgeJobOrchestrator), "3.0", new BlobPurgeJobRunRequest(23)));
        object? activityResult = await fixture.ProcessAsync(Activity(nameof(DeleteExternalBlobActivity), "3.0"));

        // Assert
        AssertRejected(orchestrationResult, activityResult, Failure.Reject);
        fixture.Store.Verify(store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConstructedWorker_KeepsItsVersionExemptionSnapshotAsync()
    {
        // Arrange
        await using Fixture fixture = new(Match.Strict, Failure.Reject);
        fixture.ReplaceConfiguredExemptionsWithBusinessNames();
        await fixture.StartAsync();

        // Act
        object? internalOrchestration = await fixture.ProcessAsync(
            Orchestration(nameof(BlobPurgeJobOrchestrator), "3.0", new BlobPurgeJobRunRequest(23)));
        object? internalActivity = await fixture.ProcessAsync(Activity(nameof(GetLargePayloadTombstonesActivity), "3.0"));
        object? businessOrchestration = await fixture.ProcessAsync(Orchestration("Business", "3.0", "input"));
        object? businessActivity = await fixture.ProcessAsync(Activity("Business", "3.0"));

        // Assert
        Assert.NotNull(Assert.Single(Assert.IsType<P.OrchestratorResponse>(internalOrchestration).Actions).ScheduleTask);
        Assert.Null(Assert.IsType<P.ActivityResponse>(internalActivity).FailureDetails);
        AssertRejected(businessOrchestration, businessActivity, Failure.Reject);
        Assert.Equal(0, fixture.BusinessCalls);
    }

    [Fact]
    public async Task InternalOrchestrator_StillHonorsCustomOrchestrationFilterAsync()
    {
        // Arrange
        await using Fixture fixture = new(Match.Strict, Failure.Reject, rejectByCustomFilter: true);
        await fixture.StartAsync();

        // Act
        object? result = await fixture.ProcessAsync(
            Orchestration(nameof(BlobPurgeJobOrchestrator), "3.0", new BlobPurgeJobRunRequest(23)));

        // Assert
        Assert.IsType<P.AbandonOrchestrationTaskRequest>(result);
        Assert.Equal(0, fixture.Invoker.FetchCount);
    }

    static void AssertRejected(object? orchestrationResult, object? activityResult, Failure failure)
    {
        if (failure == Failure.Reject)
        {
            Assert.IsType<P.AbandonOrchestrationTaskRequest>(orchestrationResult);
            Assert.IsType<P.AbandonActivityTaskRequest>(activityResult);
        }
        else
        {
            P.CompleteOrchestrationAction completion =
                Assert.Single(Assert.IsType<P.OrchestratorResponse>(orchestrationResult).Actions).CompleteOrchestration;
            Assert.Equal(P.OrchestrationStatus.Failed, completion.OrchestrationStatus);
            Assert.Equal("VersionMismatch", completion.FailureDetails.ErrorType);
            // Existing activity Fail handling returns without completing or abandoning; this fix does not change it.
            Assert.Null(activityResult);
        }
    }

    static P.WorkItem Orchestration(string name, string version, object input)
    {
        Timestamp now = Timestamp.FromDateTime(DateTime.UnixEpoch);
        return new P.WorkItem
        {
            CompletionToken = "orchestration-token",
            OrchestratorRequest = new P.OrchestratorRequest
            {
                InstanceId = "version-test",
                ExecutionId = "execution",
                NewEvents =
                {
                    new P.HistoryEvent { EventId = -1, Timestamp = now, OrchestratorStarted = new() },
                    new P.HistoryEvent
                    {
                        EventId = 0, Timestamp = now,
                        ExecutionStarted = new()
                        {
                            Name = name, Version = version, Input = JsonDataConverter.Default.Serialize(input),
                            OrchestrationInstance = new() { InstanceId = "version-test", ExecutionId = "execution" },
                        },
                    },
                },
            },
        };
    }

    static P.WorkItem Activity(string name, string version)
    {
        object input = name.Equals(nameof(GetLargePayloadTombstonesActivity), StringComparison.OrdinalIgnoreCase) ? 23
            : name.Equals(nameof(DeleteExternalBlobActivity), StringComparison.OrdinalIgnoreCase)
                ? new[] { "blob:v2:https://account.invalid/payloads/blob" }
            : name.Equals(nameof(ReportLargePayloadPurgeResultsActivity), StringComparison.OrdinalIgnoreCase)
                ? new[] { new LargePayloadPurgeResult("opaque-token", LargePayloadPurgeDisposition.Deleted) }
            : "input";
        return new P.WorkItem
        {
            CompletionToken = "activity-token",
            ActivityRequest = new()
            {
                Name = name, Version = version, TaskId = 1, Input = JsonDataConverter.Default.Serialize(new[] { input }),
                OrchestrationInstance = new() { InstanceId = "version-test", ExecutionId = "execution" },
            },
        };
    }

    sealed class Fixture : IAsyncDisposable
    {
        readonly ServiceProvider provider;
        readonly IHostedService worker;
        TaskCompletionSource activityFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Fixture(Match match, Failure failure, bool enabled = true, bool withAnotherWorker = false, bool rejectByCustomFilter = false)
        {
            this.Versioning = new() { Version = "2.0", MatchStrategy = match, FailureStrategy = failure };
            this.Store.Setup(store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PayloadDeleteOutcome.Deleted);
            ServiceCollection services = new();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(this.Store.Object);
            services.AddDurableTaskWorker("tested", builder =>
            {
                builder.UseGrpc(options =>
                {
                    options.CallInvoker = this.Invoker;
                    options.ConfigureActivityNotification(phase =>
                    {
                        if (phase == ActivityNotificationPhase.Completed)
                        {
                            this.activityFinished.TrySetResult();
                        }
                    });
                });
                builder.Configure(options => options.Versioning = this.Versioning);
                if (enabled)
                {
                    builder.UseExternalizedPayloads();
                }
                else
                {
                    builder.AddTasks(registry =>
                    {
                        registry.AddOrchestrator<BlobPurgeJobOrchestrator>();
                        registry.AddActivity<DeleteExternalBlobActivity>();
                    });
                }

                builder.AddTasks(registry =>
                {
                    string[] orchestrations = ["Business", .. InternalActivities, "BlobPurgeJobOrchestratorCustomer"];
                    foreach (string name in orchestrations)
                    {
                        registry.AddOrchestrator(name, () => new BusinessOrchestrator(this));
                    }

                    foreach (string name in new[] { "Business", nameof(BlobPurgeJobOrchestrator), "DeleteExternalBlobActivityCustomer" })
                    {
                        registry.AddActivity(name, _ => new BusinessActivity(this));
                    }
                });
                if (rejectByCustomFilter)
                {
#pragma warning disable CS0618
                    Mock<IOrchestrationFilter> filter = new();
                    filter.Setup(value => value.IsOrchestrationValidAsync(It.IsAny<OrchestrationFilterParameters>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(false);
                    builder.UseOrchestrationFilter(filter.Object);
#pragma warning restore CS0618
                }
            });
            if (withAnotherWorker)
            {
                services.AddDurableTaskWorker("enabled", builder =>
                {
                    builder.UseGrpc(options => options.CallInvoker = new WorkInvoker());
                    builder.UseExternalizedPayloads();
                });
            }

            this.provider = services.BuildServiceProvider();
            this.worker = this.provider.GetServices<IHostedService>().First();
        }

        public WorkInvoker Invoker { get; } = new();
        public Mock<PayloadStore> Store { get; } = new();
        public DurableTaskWorkerOptions.VersioningOptions Versioning { get; }
        public int BusinessCalls { get; set; }

        public void ReplaceConfiguredExemptionsWithBusinessNames()
        {
            GrpcDurableTaskWorkerOptions options =
                this.provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get("tested");
            options.Internal.VersioningExemptOrchestrations.Clear();
            options.Internal.VersioningExemptActivities.Clear();
            options.Internal.VersioningExemptOrchestrations.Add("Business");
            options.Internal.VersioningExemptActivities.Add("Business");
        }

        public async Task StartAsync()
        {
            await this.worker.StartAsync(default);
            await this.Invoker.Connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async Task<object?> ProcessAsync(P.WorkItem item)
        {
            this.activityFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.Invoker.ResponseReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await this.Invoker.Work.Writer.WriteAsync(item);
            await (item.ActivityRequest is null ? this.Invoker.ResponseReady.Task : this.activityFinished.Task)
                .WaitAsync(TimeSpan.FromSeconds(10));
            return this.Invoker.Responses.TryDequeue(out object? response) ? response : null;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await this.provider.DisposeAsync();
            }
        }
    }

    sealed class BusinessOrchestrator(Fixture fixture) : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            fixture.BusinessCalls++;
            return Task.FromResult(input);
        }
    }

    sealed class BusinessActivity(Fixture fixture) : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            fixture.BusinessCalls++;
            return Task.FromResult(input);
        }
    }

    sealed class WorkInvoker : CallInvoker
    {
        public Channel<P.WorkItem> Work { get; } = Channel.CreateUnbounded<P.WorkItem>();
        public ConcurrentQueue<object> Responses { get; } = new();
        public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResponseReady { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FetchCount { get; private set; }
        public int ReportCount { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            object response;
            switch (request)
            {
                case Empty:
                    response = new Empty();
                    break;
                case LP.GetLargePayloadTombstonesRequest:
                    this.FetchCount++;
                    response = new LP.GetLargePayloadTombstonesResponse();
                    break;
                case LP.ReportLargePayloadPurgeResultsRequest:
                    this.ReportCount++;
                    response = new LP.ReportLargePayloadPurgeResultsResponse();
                    break;
                case P.OrchestratorResponse or P.ActivityResponse:
                    this.Responses.Enqueue(request);
                    this.ResponseReady.TrySetResult();
                    response = new P.CompleteTaskResponse();
                    break;
                case P.AbandonOrchestrationTaskRequest:
                    this.Responses.Enqueue(request);
                    this.ResponseReady.TrySetResult();
                    response = new P.AbandonOrchestrationTaskResponse();
                    break;
                case P.AbandonActivityTaskRequest:
                    this.Responses.Enqueue(request);
                    response = new P.AbandonActivityTaskResponse();
                    break;
                default:
                    throw new InvalidOperationException(method.Name);
            }

            return new(Task.FromResult((TResponse)response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Assert.Equal("GetWorkItems", method.Name);
            this.Connected.TrySetResult();
            return new(new Reader<TResponse>(this.Work.Reader), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    sealed class Reader<T>(ChannelReader<P.WorkItem> work) : IAsyncStreamReader<T>
    {
        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            this.Current = (T)(object)await work.ReadAsync(cancellationToken);
            return true;
        }
    }
}
