// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates how to wrap TaskOrchestrationContext and delegate LoggerFactory
// to inner.ReplaySafeLoggerFactory so wrapper helpers can log without breaking replay safety.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReplaySafeLoggerFactorySample;

internal static class Program
{
    private static async Task Main(string[] args)
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

            Console.WriteLine("ReplaySafeLoggerFactory Sample");
            Console.WriteLine("================================");
            Console.WriteLine(useScheduler
                ? "Configured to use Durable Task Scheduler (DTS)."
                : "Configured to use local gRPC. (Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING to use DTS.)");
            Console.WriteLine();

            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(ReplaySafeLoggingOrchestration),
                input: "Seattle");

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

            Console.WriteLine($"Result: {result.ReadOutputAs<string>()}");
            Console.WriteLine();
            Console.WriteLine(
                "The wrapper delegates LoggerFactory to inner.ReplaySafeLoggerFactory, " +
                "so wrapper-level logging stays replay-safe.");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static void ConfigureDurableTask(
        HostApplicationBuilder builder,
        bool useScheduler,
        string? schedulerConnectionString)
    {
        if (useScheduler)
        {
            builder.Services.AddDurableTaskClient(clientBuilder => clientBuilder.UseDurableTaskScheduler(schedulerConnectionString!));

            builder.Services.AddDurableTaskWorker(workerBuilder =>
            {
                workerBuilder.AddTasks(tasks =>
                {
                    tasks.AddOrchestrator<ReplaySafeLoggingOrchestration>();
                    tasks.AddActivity<SayHelloActivity>();
                });

                workerBuilder.UseDurableTaskScheduler(schedulerConnectionString!);
            });
        }
        else
        {
            builder.Services.AddDurableTaskClient().UseGrpc();

            builder.Services.AddDurableTaskWorker()
                .AddTasks(tasks =>
                {
                    tasks.AddOrchestrator<ReplaySafeLoggingOrchestration>();
                    tasks.AddActivity<SayHelloActivity>();
                })
                .UseGrpc();
        }
    }
}

[DurableTask(nameof(ReplaySafeLoggingOrchestration))]
internal sealed class ReplaySafeLoggingOrchestration : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        LoggingTaskOrchestrationContext wrappedContext = new(context);
        ILogger logger = wrappedContext.CreateLogger<ReplaySafeLoggingOrchestration>();

        logger.LogInformation("Wrapping orchestration context for instance {InstanceId}.", wrappedContext.InstanceId);

        string greeting = await wrappedContext.CallActivityWithLoggingAsync<string>(nameof(SayHelloActivity), input);

        logger.LogInformation("Returning activity result for {InstanceId}.", wrappedContext.InstanceId);
        return greeting;
    }
}

[DurableTask(nameof(SayHelloActivity))]
internal sealed class SayHelloActivity : TaskActivity<string, string>
{
    readonly ILogger<SayHelloActivity> logger;

    public SayHelloActivity(ILoggerFactory loggerFactory)
    {
        this.logger = loggerFactory.CreateLogger<SayHelloActivity>();
    }

    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        this.logger.LogInformation("Generating a greeting for {Name}.", input);
        return Task.FromResult(
            $"Hello, {input}! This orchestration used ReplaySafeLoggerFactory to keep wrapper logging replay-safe.");
    }
}

internal sealed class LoggingTaskOrchestrationContext : TaskOrchestrationContext
{
    readonly TaskOrchestrationContext innerContext;

    public LoggingTaskOrchestrationContext(TaskOrchestrationContext innerContext)
    {
        this.innerContext = innerContext ?? throw new ArgumentNullException(nameof(innerContext));
    }

    public override TaskName Name => this.innerContext.Name;

    public override string InstanceId => this.innerContext.InstanceId;

    public override ParentOrchestrationInstance? Parent => this.innerContext.Parent;

    public override DateTime CurrentUtcDateTime => this.innerContext.CurrentUtcDateTime;

    public override bool IsReplaying => this.innerContext.IsReplaying;

    public override string Version => this.innerContext.Version;

    public override IReadOnlyDictionary<string, object?> Properties => this.innerContext.Properties;

    public override TaskOrchestrationEntityFeature Entities => this.innerContext.Entities;

    protected override ILoggerFactory LoggerFactory => this.innerContext.ReplaySafeLoggerFactory;

    public ILogger CreateLogger<T>()
        => this.CreateReplaySafeLogger<T>();

    public async Task<TResult> CallActivityWithLoggingAsync<TResult>(
        TaskName name,
        object? input = null,
        TaskOptions? options = null)
    {
        ILogger logger = this.CreateReplaySafeLogger<LoggingTaskOrchestrationContext>();
        logger.LogInformation("Calling activity {ActivityName} for instance {InstanceId}.", name.Name, this.InstanceId);

        TResult result = await this.CallActivityAsync<TResult>(name, input, options);

        logger.LogInformation("Activity {ActivityName} completed for instance {InstanceId}.", name.Name, this.InstanceId);
        return result;
    }

    public override T GetInput<T>()
        where T : default
        => this.innerContext.GetInput<T>()!;

    public override Task<TResult> CallActivityAsync<TResult>(
        TaskName name,
        object? input = null,
        TaskOptions? options = null)
        => this.innerContext.CallActivityAsync<TResult>(name, input, options);

    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
        => this.innerContext.CreateTimer(fireAt, cancellationToken);

    public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
        => this.innerContext.WaitForExternalEvent<T>(eventName, cancellationToken);

    public override void SendEvent(string instanceId, string eventName, object payload)
        => this.innerContext.SendEvent(instanceId, eventName, payload);

    public override void SetCustomStatus(object? customStatus)
        => this.innerContext.SetCustomStatus(customStatus);

    public override Task<TResult> CallSubOrchestratorAsync<TResult>(
        TaskName orchestratorName,
        object? input = null,
        TaskOptions? options = null)
        => this.innerContext.CallSubOrchestratorAsync<TResult>(orchestratorName, input, options);

    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
        => this.innerContext.ContinueAsNew(newInput, preserveUnprocessedEvents);

    public override Guid NewGuid()
        => this.innerContext.NewGuid();
}
