// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates orchestration and activity middleware in a standalone Durable Task worker.
// Orchestration middleware uses replay-safe logging because it runs as part of orchestrator replay.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiddlewareConsoleApp;

static class Program
{
    static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        string? schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING");
        bool useScheduler = !string.IsNullOrWhiteSpace(schedulerConnectionString);

        ConfigureDurableTask(builder, useScheduler, schedulerConnectionString);

        IHost host = builder.Build();
        await host.StartAsync();

        try
        {
            await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

            Console.WriteLine("Durable Task Middleware Sample");
            Console.WriteLine("==============================");
            Console.WriteLine(useScheduler
                ? "Configured to use Durable Task Scheduler (DTS)."
                : "Configured to use local gRPC. (Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING to use DTS.)");
            Console.WriteLine();

            GreetingRequest input = new(
                TenantId: "tenant-alpha",
                UserName: "Ada",
                Cities: new[] { "Tokyo", "London", "Seattle" });
            StartOrchestrationOptions options = new()
            {
                Tags = new Dictionary<string, string>
                {
                    ["tenant"] = input.TenantId,
                    ["scenario"] = "middleware-sample",
                },
            };

            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(GreetingOrchestration),
                input,
                options);

            Console.WriteLine($"Started orchestration instance: {instanceId}");

            using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(60));
            OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
                instanceId,
                getInputsAndOutputs: true,
                timeoutCts.Token);

            if (result.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Expected '{nameof(OrchestrationRuntimeStatus.Completed)}' but got '{result.RuntimeStatus}'.");
            }

            GreetingSummary summary = result.ReadOutputAs<GreetingSummary>()
                ?? throw new InvalidOperationException("The orchestration did not return a greeting summary.");

            Console.WriteLine();
            Console.WriteLine($"Tenant: {summary.TenantId}");
            Console.WriteLine($"Instance ID from output: {summary.InstanceId}");
            foreach (string greeting in summary.Greetings)
            {
                Console.WriteLine($"- {greeting}");
            }

            Console.WriteLine();
            Console.WriteLine("Middleware logged orchestration and activity inputs, durable context, and results.");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    static void ConfigureDurableTask(
        HostApplicationBuilder builder,
        bool useScheduler,
        string? schedulerConnectionString)
    {
        if (useScheduler)
        {
            builder.Services.AddDurableTaskClient(
                clientBuilder => clientBuilder.UseDurableTaskScheduler(schedulerConnectionString!));

            builder.Services.AddDurableTaskWorker(workerBuilder =>
            {
                ConfigureWorker(workerBuilder);
                workerBuilder.UseDurableTaskScheduler(schedulerConnectionString!);
            });
        }
        else
        {
            builder.Services.AddDurableTaskClient().UseGrpc();
            ConfigureWorker(builder.Services.AddDurableTaskWorker()).UseGrpc();
        }
    }

    static IDurableTaskWorkerBuilder ConfigureWorker(IDurableTaskWorkerBuilder workerBuilder)
    {
        return workerBuilder
            .UseOrchestrationMiddleware<GreetingOrchestrationMiddleware>()
            .UseActivityMiddleware<GreetingActivityMiddleware>()
            .AddTasks(tasks =>
            {
                tasks.AddOrchestrator<GreetingOrchestration>();
                tasks.AddActivity<SayHelloActivity>();
            });
    }
}

[DurableTask(nameof(GreetingOrchestration))]
sealed class GreetingOrchestration : TaskOrchestrator<GreetingRequest, GreetingSummary>
{
    public override async Task<GreetingSummary> RunAsync(TaskOrchestrationContext context, GreetingRequest input)
    {
        List<Task<string>> greetingTasks = new();
        foreach (string city in input.Cities)
        {
            GreetingActivityInput activityInput = new(input.TenantId, input.UserName, city);
            greetingTasks.Add(context.CallActivityAsync<string>(nameof(SayHelloActivity), activityInput));
        }

        string[] greetings = await Task.WhenAll(greetingTasks);
        return new GreetingSummary(input.TenantId, context.InstanceId, greetings);
    }
}

[DurableTask(nameof(SayHelloActivity))]
sealed class SayHelloActivity : TaskActivity<GreetingActivityInput, string>
{
    readonly ILogger<SayHelloActivity> logger;

    public SayHelloActivity(ILoggerFactory loggerFactory)
    {
        this.logger = loggerFactory.CreateLogger<SayHelloActivity>();
    }

    public override Task<string> RunAsync(TaskActivityContext context, GreetingActivityInput input)
    {
        this.logger.LogInformation(
            "Building greeting for {UserName} in {City} for tenant {TenantId}.",
            input.UserName,
            input.City,
            input.TenantId);

        return Task.FromResult($"Hello, {input.UserName} from {input.City}! Tenant: {input.TenantId}.");
    }
}

sealed class GreetingOrchestrationMiddleware : ITaskOrchestrationMiddleware
{
    public async Task InvokeAsync(
        TaskOrchestrationMiddlewareContext context,
        TaskOrchestrationMiddlewareDelegate next)
    {
        ILogger logger = context.OrchestrationContext.CreateReplaySafeLogger<GreetingOrchestrationMiddleware>();
        GreetingRequest? input = context.GetInput<GreetingRequest>();

        logger.LogInformation(
            "Starting orchestration middleware for {Name}, instance {InstanceId}, tenant {TenantId}, tags {TagCount}.",
            context.Name.Name,
            context.InstanceId,
            input?.TenantId,
            context.Tags?.Count ?? 0);

        await next(context);

        logger.LogInformation(
            "Finished orchestration middleware for {Name}, instance {InstanceId}, result type {ResultType}.",
            context.Name.Name,
            context.InstanceId,
            context.Result?.GetType().Name ?? "<null>");
    }
}

sealed class GreetingActivityMiddleware : ITaskActivityMiddleware
{
    readonly ILogger<GreetingActivityMiddleware> logger;

    public GreetingActivityMiddleware(ILogger<GreetingActivityMiddleware> logger)
    {
        this.logger = logger;
    }

    public async Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
    {
        using IDisposable? scope = this.logger.BeginScope(
            "Activity {ActivityName} for orchestration {InstanceId}",
            context.Name.Name,
            context.InstanceId);

        GreetingActivityInput? input = context.GetInput<GreetingActivityInput>();
        this.logger.LogInformation(
            "Starting activity middleware with input type {InputType} for city {City}.",
            context.InputType.Name,
            input?.City);

        await next(context);

        this.logger.LogInformation(
            "Finished activity middleware with result type {ResultType}.",
            context.Result?.GetType().Name ?? "<null>");
    }
}

sealed record GreetingRequest(string TenantId, string UserName, string[] Cities);

sealed record GreetingActivityInput(string TenantId, string UserName, string City);

sealed record GreetingSummary(string TenantId, string InstanceId, string[] Greetings);
