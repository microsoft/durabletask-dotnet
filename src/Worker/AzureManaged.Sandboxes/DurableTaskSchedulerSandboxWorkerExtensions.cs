// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.Core;
using Azure.Identity;
using Grpc.Net.Client;
using Microsoft.DurableTask.AzureManaged.Internal;
using Microsoft.DurableTask.Protobuf.Sandboxes;
using Microsoft.DurableTask.Worker.AzureManaged.Sandboxes;
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
public static class DurableTaskSchedulerSandboxWorkerExtensions
{
    const string UseSandboxWorkerNoActivitiesErrorMessage =
        "On-demand sandbox workers require at least one registered activity. " +
        "Register an activity on this worker before starting the sandbox worker.";

    const string ManagedIdentityAuthentication = "ManagedIdentity";

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

        builder.Services.AddOptions<SandboxWorkerRuntimeOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<DurableTaskSchedulerWorkerOptions>>((options, schedulerOptions) =>
            {
                ApplyRuntimeTaskHubDefault(options, schedulerOptions.Get(builder.Name).TaskHubName);
                ApplyWorkerEnvironmentOverrides(options);
            });

        builder.Services.AddOptions<DurableTaskWorkerOptions>(builder.Name)
            .PostConfigure<IOptionsMonitor<SandboxWorkerRuntimeOptions>>((options, runtimeOptions) =>
                ConfigureSandboxWorkerConcurrency(options, runtimeOptions.Get(builder.Name)));

        builder.Services.AddOptions<DurableTaskWorkerWorkItemFilters>(builder.Name)
            .PostConfigure(IncludeOnlyRegisteredActivities);

        builder.Services.AddSingleton<SandboxActivityTracker>();
        builder.Services.AddOptions<GrpcDurableTaskWorkerOptions>(builder.Name)
            .Configure<SandboxActivityTracker>((options, activityTracker) =>
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

        builder.Services.AddSingleton<IHostedService>(sp => CreateSandboxActivityWorkerRegistrationHostedService(sp, builder.Name));
        return builder;
    }

    static void IncludeOnlyRegisteredActivities(DurableTaskWorkerWorkItemFilters filters)
    {
        if (filters.Activities.Count == 0)
        {
            throw new InvalidOperationException(UseSandboxWorkerNoActivitiesErrorMessage);
        }

        filters.Orchestrations = [];
        filters.Entities = [];
    }

    static void ConfigureSandboxWorkerConcurrency(
        DurableTaskWorkerOptions options,
        SandboxWorkerRuntimeOptions runtimeOptions)
    {
        options.Concurrency.MaximumConcurrentActivityWorkItems = runtimeOptions.MaxConcurrentActivities;
        options.Concurrency.MaximumConcurrentOrchestrationWorkItems = 0;
        options.Concurrency.MaximumConcurrentEntityWorkItems = 0;
    }

    static SandboxActivityWorkerRegistrationHostedService CreateSandboxActivityWorkerRegistrationHostedService(
        IServiceProvider services,
        string builderName)
    {
        SandboxWorkerRuntimeOptions options = services.GetRequiredService<IOptionsMonitor<SandboxWorkerRuntimeOptions>>().Get(builderName);
        ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
        IHostApplicationLifetime? lifetime = services.GetService<IHostApplicationLifetime>();
        SandboxActivityTracker activityTracker = services.GetRequiredService<SandboxActivityTracker>();
        DurableTaskWorkerWorkItemFilters filters = services.GetRequiredService<IOptionsMonitor<DurableTaskWorkerWorkItemFilters>>().Get(builderName);

        return new SandboxActivityWorkerRegistrationHostedService(
            CreateSandboxActivitiesTransport(services, builderName),
            options,
            ResolveActivityFilters(filters.Activities),
            loggerFactory.CreateLogger<SandboxActivityWorkerRegistrationHostedService>(),
            lifetime,
            activityTracker);
    }

    static SandboxActivitiesGrpcTransport CreateSandboxActivitiesTransport(IServiceProvider services, string builderName)
    {
        GrpcDurableTaskWorkerOptions options = services.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>().Get(builderName);
        if (options.CallInvoker is { } callInvoker)
        {
            return new SandboxActivitiesGrpcTransport(new SandboxActivities.SandboxActivitiesClient(callInvoker));
        }

        if (options.Channel is { } channel)
        {
            return new SandboxActivitiesGrpcTransport(
                new SandboxActivities.SandboxActivitiesClient(channel.CreateCallInvoker()),
                attachTaskHubMetadata: false);
        }

        throw new InvalidOperationException("Azure Managed on-demand sandbox activities require a configured gRPC channel or call invoker.");
    }

    static void ApplyRuntimeTaskHubDefault(SandboxWorkerRuntimeOptions options, string taskHubName)
    {
        if (string.IsNullOrWhiteSpace(options.TaskHub) && !string.IsNullOrWhiteSpace(taskHubName))
        {
            options.TaskHub = taskHubName;
        }
    }

    static void ConfigureDurableTaskSchedulerFromEnvironment(IDurableTaskWorkerBuilder builder)
    {
        string endpoint = GetRequiredEnvironmentVariable(SandboxWorkerEnvironmentVariables.Endpoint);
        string taskHub = GetRequiredEnvironmentVariable(SandboxWorkerEnvironmentVariables.TaskHub);
        string authentication = GetRequiredEnvironmentVariable(SandboxWorkerEnvironmentVariables.Authentication);

        builder.UseDurableTaskScheduler(options =>
        {
            options.EndpointAddress = endpoint;
            options.TaskHubName = taskHub;
            if (UsesManagedIdentityAuthentication(authentication))
            {
                options.Credential = CreateManagedIdentityCredential();
                options.AllowInsecureCredentials = false;
            }
            else
            {
                throw new InvalidOperationException(
                    $"{SandboxWorkerEnvironmentVariables.Authentication} must be '{ManagedIdentityAuthentication}' for on-demand sandbox workers.");
            }
        });
    }

    static bool UsesManagedIdentityAuthentication(string authentication) =>
        string.Equals(authentication, ManagedIdentityAuthentication, StringComparison.OrdinalIgnoreCase);

    static ManagedIdentityCredential CreateManagedIdentityCredential() =>
        new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(GetRequiredEnvironmentVariable(SandboxWorkerEnvironmentVariables.ManagedIdentityClientId)));

    static string GetRequiredEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} must be injected by DTS for on-demand sandbox workers.")
            : value.Trim();
    }

    static void ApplyWorkerEnvironmentOverrides(SandboxWorkerRuntimeOptions options)
    {
        ValidateSandboxWorkerSandboxProvider(GetRequiredEnvironmentVariable(SandboxWorkerEnvironmentVariables.SandboxProvider));

        options.WorkerProfileId = GetRequiredEnvironmentVariable(SandboxWorkerEnvironmentVariables.WorkerProfileId);

        string? maxActivitiesValue = Environment.GetEnvironmentVariable(SandboxWorkerEnvironmentVariables.MaxActivities);
        if (maxActivitiesValue is null)
        {
            return;
        }

        if (!int.TryParse(maxActivitiesValue.Trim(), out int maxActivities) || maxActivities <= 0)
        {
            throw new InvalidOperationException(
                $"{SandboxWorkerEnvironmentVariables.MaxActivities} must be a positive integer when injected by DTS for on-demand sandbox workers.");
        }

        options.MaxConcurrentActivities = maxActivities;
    }

    static void ValidateSandboxWorkerSandboxProvider(string sandboxProvider)
    {
        if (!string.Equals(sandboxProvider, "Sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sandboxProvider, "AcaSessionPool", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{SandboxWorkerEnvironmentVariables.SandboxProvider} must be 'Sandbox' or 'AcaSessionPool' for on-demand sandbox workers.");
        }
    }

    static SandboxActivityMetadata.Activity[] ResolveActivityFilters(IReadOnlyList<DurableTaskWorkerWorkItemFilters.ActivityFilter> activityFilters)
    {
        return SandboxActivityMetadata.ResolveActivities(activityFilters.SelectMany(static filter =>
            filter.Versions.Count == 0
                ? [new SandboxActivityMetadata.Activity(filter.Name, Version: null)]
                : filter.Versions.Select(version => new SandboxActivityMetadata.Activity(filter.Name, version))));
    }
}
