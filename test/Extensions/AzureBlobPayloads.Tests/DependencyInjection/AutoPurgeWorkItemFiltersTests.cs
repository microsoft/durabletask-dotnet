// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureBlobPayloads;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Extensions.AzureBlobPayloads.Tests.DependencyInjection;

public class AutoPurgeWorkItemFiltersTests
{
    static readonly string[] PurgeActivities =
    [
        nameof(GetLargePayloadTombstonesActivity),
        nameof(DeleteExternalBlobActivity),
        nameof(ReportLargePayloadPurgeResultsActivity),
    ];

    public static TheoryData<string, bool> FilterCases
    {
        get
        {
            TheoryData<string, bool> cases = new();
            foreach (string mode in new[]
            {
                "application", "orchestrations", "activities", "entities", "included",
                "none", "null", "empty", "automatic", "replace", "clear-null", "clear-empty", "later-automatic",
            })
            {
                cases.Add(mode, false);
                cases.Add(mode, true);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task Worker_MergesPurgeNamesIntoFinalNonemptyFiltersAsync(string mode, bool externalizedFirst)
    {
        // Arrange
        using FilterHandler handler = new();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://filters.invalid", new() { HttpHandler = handler });
        ServiceCollection services = CreateServices();
        DurableTaskWorkerWorkItemFilters supplied = CreateFilters(mode);
        string suppliedBefore = System.Text.Json.JsonSerializer.Serialize(supplied);
        services.AddDurableTaskWorker("filtered", builder =>
        {
            RegisterApplication(builder, channel);
            if (externalizedFirst)
            {
                builder.UseExternalizedPayloads();
            }

            ConfigureFilters(builder, mode, supplied);
            if (!externalizedFirst)
            {
                builder.UseExternalizedPayloads();
            }

            if (mode == "replace")
            {
                builder.UseWorkItemFilters(CreateFilters("orchestrations"));
            }
            else if (mode == "clear-null")
            {
                builder.UseWorkItemFilters(null);
            }
            else if (mode == "clear-empty")
            {
                builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters());
            }
            else if (mode == "later-automatic")
            {
                builder.UseWorkItemFilters();
            }
        });

        // Act
        await using ServiceProvider provider = services.BuildServiceProvider();
        P.WorkItemFilters wire = await CaptureAsync(Assert.Single(provider.GetServices<IHostedService>()), handler);

        // Assert
        bool unfiltered = mode is "none" or "null" or "empty" or "clear-null" or "clear-empty";
        bool automatic = mode is "automatic" or "later-automatic";
        string[] orchestrations = mode is "activities" or "entities" || unfiltered ? [] : ["ApplicationOrchestrator"];
        string[] activities = mode is "orchestrations" or "entities" or "replace" || unfiltered ? [] : ["ApplicationActivity"];
        string[] entities = mode == "entities" || automatic ? ["applicationentity"] : [];
        if (!unfiltered)
        {
            orchestrations = [.. orchestrations, InternalName(nameof(BlobPurgeJobOrchestrator), mode)];
            activities = [.. activities, .. PurgeActivities.Select(name => InternalName(name, mode))];
        }

        Assert.Equal(orchestrations.Order(), wire.Orchestrations.Select(filter => filter.Name).Order());
        Assert.Equal(activities.Order(), wire.Activities.Select(filter => filter.Name).Order());
        Assert.Equal(entities, wire.Entities.Select(filter => filter.Name));
        Assert.All(wire.Orchestrations, filter => Assert.Equal(ExpectedVersions(filter.Name, mode, automatic), filter.Versions));
        Assert.All(wire.Activities, filter => Assert.Equal(ExpectedVersions(filter.Name, mode, automatic), filter.Versions));
        Assert.Equal(suppliedBefore, System.Text.Json.JsonSerializer.Serialize(supplied));
    }

    [Theory]
    [InlineData("", "filters")]
    [InlineData("", "grpc")]
    [InlineData("named", "filters")]
    [InlineData("named", "grpc")]
    public async Task Worker_MergesWithEitherOptionsSetAlreadyCachedAsync(string name, string cachedOptions)
    {
        // Arrange
        using FilterHandler handler = new();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://filters.invalid", new() { HttpHandler = handler });
        ServiceCollection services = CreateServices();
        services.AddDurableTaskWorker(name, builder =>
        {
            RegisterApplication(builder, channel);
            builder.UseExternalizedPayloads();
            builder.UseWorkItemFilters(CreateFilters("application"));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (cachedOptions == "filters")
        {
            _ = provider.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(name);
        }
        else
        {
            _ = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(name);
            provider.GetRequiredService<IOptionsMonitorCache<GrpcDurableTaskWorkerOptions>>().TryRemove(name);
            _ = provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(name);
        }

        // Act
        P.WorkItemFilters wire = await CaptureAsync(Assert.Single(provider.GetServices<IHostedService>()), handler);

        // Assert
        Assert.Single(wire.Orchestrations, filter => filter.Name == nameof(BlobPurgeJobOrchestrator));
        Assert.All(PurgeActivities, name => Assert.Single(wire.Activities, filter => filter.Name == name));
        Assert.Equal(2, wire.Orchestrations.Count);
        Assert.Equal(4, wire.Activities.Count);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("2.0", false)]
    [InlineData("2.0", true)]
    public async Task Worker_UsesStrictVersionsOnlyForNewInternalFiltersAsync(string version, bool alreadyIncluded)
    {
        // Arrange
        using FilterHandler handler = new();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://filters.invalid", new() { HttpHandler = handler });
        ServiceCollection services = CreateServices();
        services.AddDurableTaskWorker(builder =>
        {
            RegisterApplication(builder, channel);
            builder.Configure(options => options.Versioning = new()
            {
                Version = version,
                MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
            });
            builder.UseExternalizedPayloads();
            builder.UseWorkItemFilters(CreateFilters(alreadyIncluded ? "included" : "application"));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        P.WorkItemFilters wire = await CaptureAsync(Assert.Single(provider.GetServices<IHostedService>()), handler);

        // Assert
        Assert.Equal(2, wire.Orchestrations.Count);
        Assert.Equal(4, wire.Activities.Count);
        foreach (P.OrchestrationFilter filter in wire.Orchestrations)
        {
            Assert.Equal(
                filter.Name == "ApplicationOrchestrator" ? ["app-v1", "app-v2"]
                    : alreadyIncluded ? ["reserved-v1"] : new[] { version ?? string.Empty },
                filter.Versions);
        }

        foreach (P.ActivityFilter filter in wire.Activities)
        {
            Assert.Equal(
                filter.Name == "ApplicationActivity" ? ["app-v2"]
                    : alreadyIncluded ? ["reserved-v1"] : new[] { version ?? string.Empty },
                filter.Versions);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("named")]
    public async Task Worker_DoesNotChangeAnotherWorkersSharedCallerFiltersAsync(string enabledName)
    {
        // Arrange
        using FilterHandler enabledHandler = new();
        using FilterHandler otherHandler = new();
        using GrpcChannel enabledChannel = GrpcChannel.ForAddress("http://enabled.invalid", new() { HttpHandler = enabledHandler });
        using GrpcChannel otherChannel = GrpcChannel.ForAddress("http://other.invalid", new() { HttpHandler = otherHandler });
        ServiceCollection services = CreateServices();
        DurableTaskWorkerWorkItemFilters shared = CreateFilters("application");
        services.AddDurableTaskWorker(enabledName, builder =>
        {
            RegisterApplication(builder, enabledChannel);
            builder.UseExternalizedPayloads();
            builder.UseWorkItemFilters(shared);
        });
        services.AddDurableTaskWorker("other", builder =>
        {
            RegisterApplication(builder, otherChannel);
            builder.UseWorkItemFilters(shared);
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService[] workers = provider.GetServices<IHostedService>().ToArray();

        // Act
        P.WorkItemFilters enabled = await CaptureAsync(workers[0], enabledHandler);
        P.WorkItemFilters other = await CaptureAsync(workers[1], otherHandler);

        // Assert
        Assert.Equal(2, enabled.Orchestrations.Count);
        Assert.Equal(4, enabled.Activities.Count);
        Assert.Equal("ApplicationOrchestrator", Assert.Single(other.Orchestrations).Name);
        Assert.Equal("ApplicationActivity", Assert.Single(other.Activities).Name);
        Assert.Single(shared.Orchestrations);
        Assert.Single(shared.Activities);
    }

    [Fact]
    public async Task Worker_StillRejectsUnknownExplicitNamesAsync()
    {
        // Arrange
        using FilterHandler handler = new();
        using GrpcChannel channel = GrpcChannel.ForAddress("http://filters.invalid", new() { HttpHandler = handler });
        ServiceCollection services = CreateServices();
        services.AddDurableTaskWorker(builder =>
        {
            RegisterApplication(builder, channel);
            builder.UseExternalizedPayloads();
            builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
            {
                Activities = [new("NotRegistered", [])],
            });
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(
            () => provider.GetServices<IHostedService>().ToArray());

        // Assert
        Assert.Contains("NotRegistered", failure.Message);
        Assert.False(handler.Requested.Task.IsCompleted);
    }

    static ServiceCollection CreateServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<PayloadStore>(Mock.Of<PayloadStore>());
        return services;
    }

    static void RegisterApplication(IDurableTaskWorkerBuilder builder, GrpcChannel channel)
    {
        builder.UseGrpc(options => options.Channel = channel);
        builder.AddTasks(registry =>
        {
            registry.AddOrchestrator("ApplicationOrchestrator", (TaskVersion)"app-v1", () => Mock.Of<ITaskOrchestrator>());
            registry.AddOrchestrator("ApplicationOrchestrator", (TaskVersion)"app-v2", () => Mock.Of<ITaskOrchestrator>());
            registry.AddActivity("ApplicationActivity", (TaskVersion)"app-v2", () => Mock.Of<ITaskActivity>());
            registry.AddEntity("applicationentity", Mock.Of<ITaskEntity>());
        });
    }

    static DurableTaskWorkerWorkItemFilters CreateFilters(string mode)
    {
        DurableTaskWorkerWorkItemFilters filters = new()
        {
            Orchestrations = mode is "activities" or "entities" ? [] : [new("ApplicationOrchestrator", ["app-v1", "app-v2"])],
            Activities = mode is "orchestrations" or "entities" ? [] : [new("ApplicationActivity", ["app-v2"])],
            Entities = mode == "entities" ? [new("applicationentity")] : [],
        };
        if (mode == "included")
        {
            filters.Orchestrations = [.. filters.Orchestrations, new(nameof(BlobPurgeJobOrchestrator).ToLowerInvariant(), ["reserved-v1"])];
            filters.Activities = [.. filters.Activities, .. PurgeActivities.Select(name =>
                new DurableTaskWorkerWorkItemFilters.ActivityFilter(name.ToLowerInvariant(), ["reserved-v1"]))];
        }

        return filters;
    }

    static void ConfigureFilters(IDurableTaskWorkerBuilder builder, string mode, DurableTaskWorkerWorkItemFilters supplied)
    {
        switch (mode)
        {
            case "none":
                break;
            case "null":
                builder.UseWorkItemFilters(null);
                break;
            case "empty":
                builder.UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters());
                break;
            case "automatic":
                builder.UseWorkItemFilters();
                break;
            default:
                builder.UseWorkItemFilters(supplied);
                break;
        }
    }

    static string InternalName(string name, string mode) => mode == "included" ? name.ToLowerInvariant() : name;

    static string[] ExpectedVersions(string name, string mode, bool automatic)
        => name == "ApplicationOrchestrator" ? ["app-v1", "app-v2"]
            : name == "ApplicationActivity" ? ["app-v2"]
            : mode == "included" && !automatic ? ["reserved-v1"] : [];

    static async Task<P.WorkItemFilters> CaptureAsync(IHostedService worker, FilterHandler handler)
    {
        try
        {
            await worker.StartAsync(CancellationToken.None);
            return (await handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10))).WorkItemFilters;
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    sealed class FilterHandler : HttpMessageHandler
    {
        public TaskCompletionSource<P.GetWorkItemsRequest> Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] frame = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(0, frame[0]);
            Assert.Equal(frame.Length - 5, BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(1, 4)));
            if (request.RequestUri!.AbsolutePath.EndsWith("/GetWorkItems", StringComparison.Ordinal))
            {
                this.Requested.TrySetResult(P.GetWorkItemsRequest.Parser.ParseFrom(frame.AsSpan(5).ToArray()));
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Work-item request must end by cancellation.");
            }

            Assert.EndsWith("/Hello", request.RequestUri.AbsolutePath);
            HttpResponseMessage response = new(HttpStatusCode.OK) { Version = new(2, 0), Content = new ByteArrayContent(new byte[5]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.TryAddWithoutValidation("grpc-status", "0");
            return response;
        }
    }
}
