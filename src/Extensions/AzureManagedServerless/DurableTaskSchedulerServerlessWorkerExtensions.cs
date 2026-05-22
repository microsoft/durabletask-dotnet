// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.Identity;
using Grpc.Net.Client;
using Microsoft.DurableTask.Protobuf.Serverless;
using Microsoft.DurableTask.Worker.AzureManaged.Serverless;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.AzureManaged;

/// <summary>
/// Extension methods for configuring Azure Managed Durable Task workers with serverless activity support.
/// </summary>
public static class DurableTaskSchedulerServerlessWorkerExtensions
{
    /// <summary>
    /// Declares serverless activities with DTS and excludes them from local execution.
    /// Call this on the local coordinator worker — not on the sandbox worker binary.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="configure">Callback to configure serverless activity behavior.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder DeclareServerlessActivities(
        this IDurableTaskWorkerBuilder builder,
        Action<ServerlessOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);

        builder.Services.AddOptions<ServerlessOptions>(builder.Name)
            .Configure(configure)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
                ApplyTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName));

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure<IOptionsMonitor<ServerlessOptions>>(
                (filters, serverlessOptions) => ExcludeServerlessActivitiesFromLocalExecution(filters, serverlessOptions.Get(builder.Name)));

        builder.Services.AddSingleton<IHostedService>(sp => CreateServerlessActivityDeclarationHostedService(sp, builder.Name));
        return builder;
    }

    /// <summary>
    /// Configures this worker as a serverless activity worker that connects to DTS to receive and execute
    /// serverless activities. Use this on a dedicated worker binary that runs inside serverless infrastructure.
    /// Runtime configuration is read from environment variables injected by DTS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is for separate worker binaries only. The coordinator uses <see cref="DeclareServerlessActivities"/>
    /// to declare and provision the serverless activity configuration.
    /// </para>
    /// <para>
    /// Required environment variables injected automatically by DTS:
    /// <list type="bullet">
    /// <item><c>DTS_ENDPOINT</c> — canonical scheduler endpoint</item>
    /// <item><c>DTS_TASK_HUB</c> — task hub name from the declaration</item>
    /// <item><c>DTS_SUBSTRATE</c> — identifies the sandbox substrate</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseServerlessWorker(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        ConfigureDurableTaskSchedulerFromEnvironment(builder);
        builder.UseWorkItemFilters();

        builder.Services.AddOptions<ServerlessOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
                ApplyWorkerEnvironmentOverrides(options);
            });

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure(IncludeOnlyRegisteredActivities);

        builder.Services.AddSingleton<ServerlessActivityTracker>();
        builder.Services.AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .Configure<ServerlessActivityTracker>((options, activityTracker) =>
                options.ConfigureActivityNotification(phase =>
                {
                    if (phase == ActivityNotificationPhase.Started)
                    {
                        activityTracker.NotifyActivityStarted();
                    }
                    else if (phase == ActivityNotificationPhase.Completed)
                    {
                        activityTracker.NotifyActivityCompleted();
                    }
                }));

        builder.Services.AddSingleton<IHostedService>(sp => CreateServerlessActivityWorkerRegistrationHostedService(sp, builder.Name));
        return builder;
    }

    static void ExcludeServerlessActivitiesFromLocalExecution(DurableTaskWorkerWorkItemFilters filters, ServerlessOptions options)
    {
        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames);
        if (activityNames.Length == 0)
        {
            return;
        }

        filters.ExcludedActivities = MergeActivityFilters(filters.ExcludedActivities, activityNames);
    }

    static void IncludeOnlyRegisteredActivities(DurableTaskWorkerWorkItemFilters filters)
    {
        filters.Orchestrations = [];
        filters.ExcludedActivities = [];
        filters.Entities = [];
    }

    static ServerlessActivityDeclarationHostedService CreateServerlessActivityDeclarationHostedService(
        IServiceProvider services,
        string builderName)
    {
        ServerlessOptions options = services.GetRequiredService<IOptionsMonitor<ServerlessOptions>>().Get(builderName);
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();

        return new ServerlessActivityDeclarationHostedService(
            CreateServerlessActivitiesClient(services, builderName),
            options,
            loggerFactory.CreateLogger<ServerlessActivityDeclarationHostedService>());
    }

    static ServerlessActivityWorkerRegistrationHostedService CreateServerlessActivityWorkerRegistrationHostedService(
        IServiceProvider services,
        string builderName)
    {
        ServerlessOptions options = services.GetRequiredService<IOptionsMonitor<ServerlessOptions>>().Get(builderName);
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
        IHostApplicationLifetime? lifetime = services.GetService<IHostApplicationLifetime>();
        ServerlessActivityTracker activityTracker = services.GetRequiredService<ServerlessActivityTracker>();
        DurableTaskWorkerWorkItemFilters filters = services.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(builderName);

        return new ServerlessActivityWorkerRegistrationHostedService(
            CreateServerlessActivitiesClient(services, builderName),
            options,
            ResolveActivityFilterNames(filters.Activities),
            loggerFactory.CreateLogger<ServerlessActivityWorkerRegistrationHostedService>(),
            lifetime,
            activityTracker);
    }

    static ServerlessActivitiesClientAdapter CreateServerlessActivitiesClient(IServiceProvider services, string builderName)
    {
        GrpcDurableTaskWorkerOptions options = services.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(builderName);
        if (options.CallInvoker is { } callInvoker)
        {
            return new ServerlessActivitiesClientAdapter(new ServerlessActivities.ServerlessActivitiesClient(callInvoker));
        }

        if (options.Channel is { } channel)
        {
            return new ServerlessActivitiesClientAdapter(
                new ServerlessActivities.ServerlessActivitiesClient(channel.CreateCallInvoker()),
                attachTaskHubMetadata: false);
        }

        throw new InvalidOperationException("Azure Managed serverless activities require a configured gRPC channel or call invoker.");
    }

    static void ApplyTaskHubDefault(ServerlessOptions options, string taskHubName)
    {
        if (string.IsNullOrWhiteSpace(options.TaskHub) && !string.IsNullOrWhiteSpace(taskHubName))
        {
            options.TaskHub = taskHubName;
        }
    }

    static void ConfigureDurableTaskSchedulerFromEnvironment(IDurableTaskWorkerBuilder builder)
    {
        string? endpoint = Environment.GetEnvironmentVariable("DTS_ENDPOINT");
        string? taskHub = Environment.GetEnvironmentVariable("DTS_TASK_HUB");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(taskHub))
        {
            return;
        }

        // Private preview: DTS-owned sandbox workers authenticate with the injected
        // managed identity via DefaultAzureCredential. Revisit this if customer-owned
        // worker identities or non-default auth modes are introduced.
        builder.UseDurableTaskScheduler(endpoint.Trim(), taskHub.Trim(), new DefaultAzureCredential());
    }

    static void ApplyWorkerEnvironmentOverrides(ServerlessOptions options)
    {
        // Auto-detect worker mode from DTS_SUBSTRATE, which the backend injects when
        // launching a sandbox. This is the authoritative signal that this process is a sandbox worker.
        string? substrate = Environment.GetEnvironmentVariable("DTS_SUBSTRATE");
        if (string.Equals(substrate, "Sandbox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(substrate, "AcaSessionPool", StringComparison.OrdinalIgnoreCase))
        {
            options.Mode = ServerlessMode.ServerlessInclude;
        }

        ApplyWorkerProfileEnvironmentOverride(profile => options.WorkerProfileId = profile);

        if (int.TryParse(Environment.GetEnvironmentVariable("DTS_SERVERLESS_MAX_ACTIVITIES"), out int maxActivities) && maxActivities > 0)
        {
            options.MaxConcurrentActivities = maxActivities;
        }
    }

    static void ApplyWorkerProfileEnvironmentOverride(Action<string> setWorkerProfileId)
    {
        string? workerProfileId = Environment.GetEnvironmentVariable("DTS_WORKER_PROFILE_ID");
        if (!string.IsNullOrWhiteSpace(workerProfileId))
        {
            setWorkerProfileId(workerProfileId.Trim());
        }
    }

    static DurableTaskWorkerWorkItemFilters.ActivityFilter[] MergeActivityFilters(
        IReadOnlyList<DurableTaskWorkerWorkItemFilters.ActivityFilter> existingFilters,
        IEnumerable<string> activityNames)
    {
        Dictionary<string, DurableTaskWorkerWorkItemFilters.ActivityFilter> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (DurableTaskWorkerWorkItemFilters.ActivityFilter filter in existingFilters)
        {
            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                merged[filter.Name] = filter;
            }
        }

        foreach (string activityName in activityNames)
        {
            merged[activityName] = new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = activityName };
        }

        return merged.Values.ToArray();
    }

    static string[] ResolveActivityFilterNames(IReadOnlyList<DurableTaskWorkerWorkItemFilters.ActivityFilter> activityFilters)
    {
        return activityFilters
            .Select(static filter => filter.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
