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
    /// Enables annotation-based serverless activity declarations with DTS and excludes annotated
    /// serverless activities from local execution.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder EnableServerlessActivities(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure(ExcludeAnnotatedServerlessActivitiesFromLocalExecution);

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
    /// This method is for separate worker binaries only. The coordinator uses <see cref="EnableServerlessActivities"/>
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

        builder.Services.AddOptions<ServerlessWorkerRuntimeOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyRuntimeTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
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

    static void ExcludeAnnotatedServerlessActivitiesFromLocalExecution(DurableTaskWorkerWorkItemFilters filters)
    {
        string[] activityNames = ServerlessActivityAnnotationResolver.ResolveActivityNames();
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
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
        ServerlessWorkerRuntimeOptions runtimeOptions = services.GetRequiredService<IOptionsMonitor<ServerlessWorkerRuntimeOptions>>().Get(builderName);
        DurableTaskSchedulerWorkerOptions schedulerOptions = services.GetRequiredService<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>().Get(builderName);

        return new ServerlessActivityDeclarationHostedService(
            CreateServerlessActivitiesClient(services, builderName),
            ServerlessActivityAnnotationResolver.Resolve(schedulerOptions.TaskHubName),
            runtimeOptions,
            loggerFactory.CreateLogger<ServerlessActivityDeclarationHostedService>());
    }

    static ServerlessActivityWorkerRegistrationHostedService CreateServerlessActivityWorkerRegistrationHostedService(
        IServiceProvider services,
        string builderName)
    {
        ServerlessWorkerRuntimeOptions options = services.GetRequiredService<IOptionsMonitor<ServerlessWorkerRuntimeOptions>>().Get(builderName);
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

    static void ApplyRuntimeTaskHubDefault(ServerlessWorkerRuntimeOptions options, string taskHubName)
    {
        if (string.IsNullOrWhiteSpace(options.TaskHub) && !string.IsNullOrWhiteSpace(taskHubName))
        {
            options.TaskHub = taskHubName;
        }
    }

    static void ConfigureDurableTaskSchedulerFromEnvironment(IDurableTaskWorkerBuilder builder)
    {
        string endpoint = GetRequiredEnvironmentVariable("DTS_ENDPOINT");
        string taskHub = GetRequiredEnvironmentVariable("DTS_TASK_HUB");

        builder.UseDurableTaskScheduler(options =>
        {
            options.EndpointAddress = endpoint;
            options.TaskHubName = taskHub;
            options.AllowInsecureCredentials = true;
        });
    }

    static string GetRequiredEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} must be injected by DTS for serverless workers.")
            : value.Trim();
    }

    static void ApplyWorkerEnvironmentOverrides(ServerlessWorkerRuntimeOptions options)
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
