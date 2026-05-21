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
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is for separate worker binaries only. The coordinator uses <see cref="DeclareServerlessActivities"/>
    /// to declare and provision the serverless activity configuration.
    /// </para>
    /// <para>
    /// Pass any environment-derived values explicitly through the configure callback or pre-configured options.
    /// </para>
    /// </remarks>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseServerlessWorker(this IDurableTaskWorkerBuilder builder)
        => UseServerlessWorker(builder, static _ => { });

    /// <summary>
    /// Configures this worker as a serverless activity worker that connects to DTS to receive and execute
    /// serverless activities. Use this on a dedicated worker binary that runs inside serverless infrastructure.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="configure">Callback to configure serverless worker behavior.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseServerlessWorker(
        this IDurableTaskWorkerBuilder builder,
        Action<ServerlessOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);

        builder.Services.AddOptions<ServerlessOptions>(builder.Name)
            .Configure(configure)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
                options.Mode = ServerlessMode.ServerlessInclude;
            });

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure<IOptionsMonitor<ServerlessOptions>>(
                (filters, serverlessOptions) => IncludeOnlyServerlessActivities(filters, serverlessOptions.Get(builder.Name)));

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
        builder.Services.AddSingleton<IHostedService>(sp => CreateServerlessWakeupServer(sp, builder.Name));
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
        ServerlessActivityTracker activityTracker = services.GetRequiredService<ServerlessActivityTracker>();

        return new ServerlessActivityWorkerRegistrationHostedService(
            CreateServerlessActivitiesClient(services, builderName),
            options,
            loggerFactory.CreateLogger<ServerlessActivityWorkerRegistrationHostedService>(),
            lifetime,
            activityTracker);
    }

    static ServerlessWakeupServer CreateServerlessWakeupServer(IServiceProvider services, string builderName)
    {
        ServerlessOptions options = services.GetRequiredService<IOptionsMonitor<ServerlessOptions>>().Get(builderName);
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();

        return new ServerlessWakeupServer(
            options,
            loggerFactory.CreateLogger<ServerlessWakeupServer>());
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
