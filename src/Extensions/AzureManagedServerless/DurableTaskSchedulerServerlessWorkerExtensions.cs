// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
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
    /// Declares serverless activities with DTS, excludes them from local execution, and propagates the
    /// activity list to sandbox workers via the <c>DTS_SERVERLESS_ACTIVITIES</c> environment variable.
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
    /// All configuration is read from environment variables injected by the backend and coordinator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is for separate worker binaries only. The coordinator uses <see cref="DeclareServerlessActivities"/>
    /// to declare and provision the serverless activity configuration.
    /// </para>
    /// <para>
    /// Required environment variables (injected automatically by the backend and coordinator):
    /// <list type="bullet">
    /// <item><c>DTS_SUBSTRATE</c> — identifies the sandbox substrate (injected by backend)</item>
    /// <item><c>DTS_SERVERLESS_ACTIVITIES</c> — comma-separated activity names to execute (injected by coordinator)</item>
    /// <item><c>DTS_TASK_HUB</c> — task hub name (injected by coordinator)</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseServerlessWorker(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        builder.Services.AddOptions<ServerlessOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
                ApplyWorkerEnvironmentOverrides(options);
            });

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure<IOptionsMonitor<ServerlessOptions>>(
                (filters, serverlessOptions) => IncludeOnlyServerlessActivities(filters, serverlessOptions.Get(builder.Name)));

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

    static void IncludeOnlyServerlessActivities(DurableTaskWorkerWorkItemFilters filters, ServerlessOptions options)
    {
        string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames);
        if (activityNames.Length == 0)
        {
            return;
        }

        filters.Orchestrations = [];
        filters.Activities = activityNames
            .Select(static name => new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = name })
            .ToArray();
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

        return new ServerlessActivityWorkerRegistrationHostedService(
            CreateServerlessActivitiesClient(services, builderName),
            options,
            loggerFactory.CreateLogger<ServerlessActivityWorkerRegistrationHostedService>(),
            lifetime);
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
            return new ServerlessActivitiesClientAdapter(new ServerlessActivities.ServerlessActivitiesClient(channel.CreateCallInvoker()));
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

        // DTS_SERVERLESS_ACTIVITIES is injected by the coordinator into the sandbox environment.
        ApplyActivityNameEnvironmentOverride(options.ActivityNames);
        ApplyWorkerProfileEnvironmentOverride(profile => options.WorkerProfileId = profile);

        if (int.TryParse(Environment.GetEnvironmentVariable("DTS_SERVERLESS_MAX_ACTIVITIES"), out int maxActivities) && maxActivities > 0)
        {
            options.MaxConcurrentActivities = maxActivities;
        }
    }

    static void ApplyActivityNameEnvironmentOverride(ICollection<string> activityNames)
    {
        string? serverlessActivities = Environment.GetEnvironmentVariable("DTS_SERVERLESS_ACTIVITIES");
        if (serverlessActivities is null)
        {
            return;
        }

        activityNames.Clear();
        foreach (string name in serverlessActivities
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal))
        {
            activityNames.Add(name);
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
}
