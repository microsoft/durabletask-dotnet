// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.Core;
using Azure.Identity;
using Grpc.Net.Client;
using Microsoft.DurableTask.Protobuf.OnDemandSandbox;
using Microsoft.DurableTask.Worker.AzureManaged.OnDemandSandbox;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Grpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.AzureManaged;

/// <summary>
/// Extension methods for configuring Azure Managed Durable Task workers with on-demand sandbox activity support.
/// </summary>
public static class DurableTaskSchedulerOnDemandSandboxWorkerExtensions
{
    const string ExcludeSandboxActivitiesWorkerRuntimeErrorMessage =
        "On-demand sandbox activity exclusion is for normal app workers. " +
        "DTS on-demand sandbox workers should use UseSandboxWorker instead.";

    const string UseSandboxWorkerNoActivitiesErrorMessage =
        "On-demand sandbox workers require at least one registered activity. " +
        "Register an activity on this worker before starting the sandbox worker.";

    /// <summary>
    /// Configures this worker to exclude activities declared for on-demand sandbox execution from local execution.
    /// Use <see cref="Microsoft.DurableTask.Client.AzureManaged.OnDemandSandboxActivitiesClient.EnableSandboxActivitiesAsync(string, CancellationToken)"/>
    /// from the client process to declare on-demand sandbox activities with DTS.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder ExcludeOnDemandSandboxActivities(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);
        ThrowIfOnDemandSandboxWorkerRuntime();

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((filters, schedulerOptions) =>
                ExcludeDeclaredOnDemandSandboxActivitiesFromLocalExecution(filters, schedulerOptions.Get(builder.Name).TaskHubName));

        return builder;
    }

    /// <summary>
    /// Configures this worker as an on-demand sandbox activity worker that connects to DTS to receive and execute
    /// on-demand sandbox activities. Use this on a dedicated worker binary that runs inside sandbox infrastructure.
    /// Runtime configuration is read from environment variables injected by DTS.
    /// </summary>
    /// <param name="builder">The Durable Task worker builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseSandboxWorker(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder);

        ConfigureDurableTaskSchedulerFromEnvironment(builder);
        builder.UseWorkItemFilters();

        builder.Services.AddOptions<OnDemandSandboxWorkerRuntimeOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyRuntimeTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
                ApplyWorkerEnvironmentOverrides(options);
            });

        builder.Services.AddOptions<DurableTaskWorkerOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<OnDemandSandboxWorkerRuntimeOptions>>((options, runtimeOptions) =>
                ConfigureOnDemandSandboxWorkerConcurrency(options, runtimeOptions.Get(builder.Name)));

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure(IncludeOnlyRegisteredActivities);

        builder.Services.AddSingleton<OnDemandSandboxActivityTracker>();
        builder.Services.AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .Configure<OnDemandSandboxActivityTracker>((options, activityTracker) =>
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

        builder.Services.AddSingleton<IHostedService>(sp => CreateOnDemandSandboxActivityWorkerRegistrationHostedService(sp, builder.Name));
        return builder;
    }

    static void ExcludeDeclaredOnDemandSandboxActivitiesFromLocalExecution(
        DurableTaskWorkerWorkItemFilters filters,
        string taskHub)
    {
        string[] activityNames = OnDemandSandboxActivityDeclarationResolver.ResolveDeclaredActivityNames(taskHub);
        if (activityNames.Length == 0)
        {
            return;
        }

        filters.ExcludedActivities = MergeActivityFilters(filters.ExcludedActivities, activityNames);
    }

    static void IncludeOnlyRegisteredActivities(DurableTaskWorkerWorkItemFilters filters)
    {
        if (filters.Activities.Count == 0)
        {
            throw new InvalidOperationException(UseSandboxWorkerNoActivitiesErrorMessage);
        }

        filters.Orchestrations = [];
        filters.ExcludedActivities = [];
        filters.Entities = [];
    }

    static void ConfigureOnDemandSandboxWorkerConcurrency(
        DurableTaskWorkerOptions options,
        OnDemandSandboxWorkerRuntimeOptions runtimeOptions)
    {
        options.Concurrency.MaximumConcurrentActivityWorkItems = runtimeOptions.MaxConcurrentActivities;
        options.Concurrency.MaximumConcurrentOrchestrationWorkItems = 0;
        options.Concurrency.MaximumConcurrentEntityWorkItems = 0;
    }

    static void ThrowIfOnDemandSandboxWorkerRuntime()
    {
        if (IsOnDemandSandboxWorkerSubstrate(Environment.GetEnvironmentVariable("DTS_SUBSTRATE")))
        {
            throw new InvalidOperationException(ExcludeSandboxActivitiesWorkerRuntimeErrorMessage);
        }
    }

    static OnDemandSandboxActivityWorkerRegistrationHostedService CreateOnDemandSandboxActivityWorkerRegistrationHostedService(
        IServiceProvider services,
        string builderName)
    {
        OnDemandSandboxWorkerRuntimeOptions options = services.GetRequiredService<IOptionsMonitor<OnDemandSandboxWorkerRuntimeOptions>>().Get(builderName);
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
        IHostApplicationLifetime? lifetime = services.GetService<IHostApplicationLifetime>();
        OnDemandSandboxActivityTracker activityTracker = services.GetRequiredService<OnDemandSandboxActivityTracker>();
        DurableTaskWorkerWorkItemFilters filters = services.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(builderName);

        return new OnDemandSandboxActivityWorkerRegistrationHostedService(
            CreateOnDemandSandboxActivitiesClient(services, builderName),
            options,
            ResolveActivityFilterNames(filters.Activities),
            loggerFactory.CreateLogger<OnDemandSandboxActivityWorkerRegistrationHostedService>(),
            lifetime,
            activityTracker);
    }

    static OnDemandSandboxActivitiesClientAdapter CreateOnDemandSandboxActivitiesClient(IServiceProvider services, string builderName)
    {
        GrpcDurableTaskWorkerOptions options = services.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(builderName);
        if (options.CallInvoker is { } callInvoker)
        {
            return new OnDemandSandboxActivitiesClientAdapter(new OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(callInvoker));
        }

        if (options.Channel is { } channel)
        {
            return new OnDemandSandboxActivitiesClientAdapter(
                new OnDemandSandboxActivities.OnDemandSandboxActivitiesClient(channel.CreateCallInvoker()),
                attachTaskHubMetadata: false);
        }

        throw new InvalidOperationException("Azure Managed on-demand sandbox activities require a configured gRPC channel or call invoker.");
    }

    static void ApplyRuntimeTaskHubDefault(OnDemandSandboxWorkerRuntimeOptions options, string taskHubName)
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
            if (UsesManagedIdentityAuthentication(Environment.GetEnvironmentVariable("DTS_AUTHENTICATION")))
            {
                options.Credential = CreateManagedIdentityCredential();
                options.AllowInsecureCredentials = false;
            }
        });
    }

    static bool UsesManagedIdentityAuthentication(string? authentication) =>
        string.Equals(authentication, "ManagedIdentity", StringComparison.OrdinalIgnoreCase);

    static TokenCredential CreateManagedIdentityCredential() =>
        new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(GetRequiredEnvironmentVariable("DTS_UMI_CLIENT_ID")));

    static string GetRequiredEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} must be injected by DTS for on-demand sandbox workers.")
            : value.Trim();
    }

    static void ApplyWorkerEnvironmentOverrides(OnDemandSandboxWorkerRuntimeOptions options)
    {
        // Auto-detect worker mode from DTS_SUBSTRATE, which the backend injects when
        // launching a sandbox. This is the authoritative signal that this process is a sandbox worker.
        if (IsOnDemandSandboxWorkerSubstrate(Environment.GetEnvironmentVariable("DTS_SUBSTRATE")))
        {
            options.Mode = OnDemandSandboxMode.OnDemandSandboxInclude;
        }

        ApplyWorkerProfileEnvironmentOverride(profile => options.WorkerProfileId = profile);

        if (int.TryParse(Environment.GetEnvironmentVariable("DTS_ON_DEMAND_SANDBOX_MAX_ACTIVITIES"), out int maxActivities) && maxActivities > 0)
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

    static bool IsOnDemandSandboxWorkerSubstrate(string? substrate)
        => string.Equals(substrate, "Sandbox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(substrate, "AcaSessionPool", StringComparison.OrdinalIgnoreCase);

    static DurableTaskWorkerWorkItemFilters.ActivityFilter[] MergeActivityFilters(
        IReadOnlyList<DurableTaskWorkerWorkItemFilters.ActivityFilter> existingFilters,
        IEnumerable<string> activityNames)
    {
        Dictionary<string, DurableTaskWorkerWorkItemFilters.ActivityFilter> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (DurableTaskWorkerWorkItemFilters.ActivityFilter filter in existingFilters.Where(static filter => !string.IsNullOrWhiteSpace(filter.Name)))
        {
            merged[filter.Name] = filter;
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
