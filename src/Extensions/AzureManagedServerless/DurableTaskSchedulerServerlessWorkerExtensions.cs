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
    /// Configures serverless activity declaration, local exclusion, and serverless worker registration.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <param name="configure">Optional callback to configure serverless activity behavior.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseServerlessActivities(
        this IDurableTaskWorkerBuilder builder,
        Action<ServerlessOptions>? configure = null)
    {
        Check.NotNull(builder);

        builder.Services.AddOptions<ServerlessOptions>(builder.Name)
            .Configure(configure ?? (_ => { }))
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
                ApplyServerlessEnvironmentOverrides(options);
            });

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure<IOptionsMonitor<ServerlessOptions>>(
                (filters, serverlessOptions) =>
                {
                    ServerlessOptions options = serverlessOptions.Get(builder.Name);
                    string[] activityNames = ServerlessActivityConfiguration.ResolveActivityNames(options.ActivityNames);
                    if (activityNames.Length == 0)
                    {
                        return;
                    }

                    if (options.Mode == ServerlessMode.ServerlessInclude)
                    {
                        filters.Orchestrations = [];
                        filters.Activities = activityNames
                            .Select(static name => new DurableTaskWorkerWorkItemFilters.ActivityFilter { Name = name })
                            .ToArray();
                        filters.ExcludedActivities = [];
                        filters.Entities = [];
                        return;
                    }

                    filters.ExcludedActivities = MergeActivityFilters(filters.ExcludedActivities, activityNames);
                });

        builder.Services.AddSingleton<IHostedService>(sp => CreateServerlessActivityDeclarationHostedService(sp, builder.Name));
        builder.Services.AddSingleton<IHostedService>(sp => CreateServerlessActivityWorkerRegistrationHostedService(sp, builder.Name));
        return builder;
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

    static void ApplyServerlessEnvironmentOverrides(ServerlessOptions options)
    {
        string? mode = Environment.GetEnvironmentVariable("DTS_SERVERLESS_MODE");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            options.Mode = string.Equals(mode, "serverless-worker", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, nameof(ServerlessMode.ServerlessInclude), StringComparison.OrdinalIgnoreCase)
                    ? ServerlessMode.ServerlessInclude
                    : ServerlessMode.LocalExclude;
        }

        ApplyActivityNameEnvironmentOverride(options.ActivityNames);
        ApplyWorkerProfileEnvironmentOverride(profile => options.WorkerProfileId = profile);

        string? image = Environment.GetEnvironmentVariable("DTS_SERVERLESS_ACTIVITY_IMAGE");
        if (!string.IsNullOrWhiteSpace(image))
        {
            options.ContainerImage = image;
        }

        string? cpu = Environment.GetEnvironmentVariable("DTS_SERVERLESS_CPU");
        if (!string.IsNullOrWhiteSpace(cpu))
        {
            options.Cpu = cpu.Trim();
        }

        string? memory = Environment.GetEnvironmentVariable("DTS_SERVERLESS_MEMORY");
        if (!string.IsNullOrWhiteSpace(memory))
        {
            options.Memory = memory.Trim();
        }

        string? launchCommand = Environment.GetEnvironmentVariable("DTS_SERVERLESS_LAUNCH_COMMAND");
        if (!string.IsNullOrWhiteSpace(launchCommand))
        {
            options.LaunchCommand = launchCommand;
        }

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
