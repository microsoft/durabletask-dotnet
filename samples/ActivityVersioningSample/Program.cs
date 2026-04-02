// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates activity versioning with [DurableTaskVersion].
// Versioned orchestrators and versioned activities can share the same logical
// durable task names in one worker process. Plain activity calls inherit the
// orchestration instance version by default, while version-qualified helpers
// can explicitly override that routing when needed.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Read the DTS connection string from configuration.
string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "Set DURABLE_TASK_SCHEDULER_CONNECTION_STRING. " +
        "For the local emulator: Endpoint=http://localhost:8080;TaskHub=default;Authentication=None");

// AddAllGeneratedTasks() registers every [DurableTask]-annotated class in this
// project, including both versions of the orchestration and activity classes.
builder.Services.AddDurableTaskWorker(wb =>
{
    wb.AddTasks(tasks => tasks.AddAllGeneratedTasks());
    wb.UseDurableTaskScheduler(connectionString);
});

// Configure the client. Unlike worker-level versioning, the client does not
// stamp a single default version for every instance.
builder.Services.AddDurableTaskClient(cb => cb.UseDurableTaskScheduler(connectionString));

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

Console.WriteLine("=== Activity versioning ([DurableTaskVersion]) ===");
Console.WriteLine();

Console.WriteLine("Scheduling CheckoutWorkflow v1 ...");
string v1Id = await client.ScheduleNewCheckoutWorkflow_1InstanceAsync(5);
OrchestrationMetadata v1 = await client.WaitForInstanceCompletionAsync(v1Id, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {v1.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Scheduling CheckoutWorkflow v2 ...");
string v2Id = await client.ScheduleNewCheckoutWorkflow_2InstanceAsync(5);
OrchestrationMetadata v2 = await client.WaitForInstanceCompletionAsync(v2Id, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {v2.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Scheduling CheckoutWorkflow v2 with explicit ShippingQuote v1 override ...");
string overrideId = await client.ScheduleNewOrchestrationInstanceAsync(
    "ExplicitOverrideCheckoutWorkflow",
    input: 5,
    new StartOrchestrationOptions
    {
        Version = new TaskVersion("2"),
    });
OrchestrationMetadata overrideResult = await client.WaitForInstanceCompletionAsync(overrideId, getInputsAndOutputs: true);
Console.WriteLine($"  Result: {overrideResult.ReadOutputAs<string>()}");
Console.WriteLine();

Console.WriteLine("Done! Both versions ran in the same worker process.");
Console.WriteLine("Default activity calls inherit the orchestration version, but versioned helpers can explicitly override it.");

await host.StopAsync();

/// <summary>
/// CheckoutWorkflow v1 - default activity calls inherit orchestration version "1".
/// </summary>
[DurableTask("CheckoutWorkflow")]
[DurableTaskVersion("1")]
public sealed class CheckoutWorkflowV1 : TaskOrchestrator<int, string>
{
    /// <inheritdoc />
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int itemCount)
    {
        string quote = await context.CallActivityAsync<string>("ShippingQuote", itemCount);
        return $"Workflow v1 -> {quote}";
    }
}

/// <summary>
/// CheckoutWorkflow v2 - default activity calls inherit orchestration version "2".
/// </summary>
[DurableTask("CheckoutWorkflow")]
[DurableTaskVersion("2")]
public sealed class CheckoutWorkflowV2 : TaskOrchestrator<int, string>
{
    /// <inheritdoc />
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int itemCount)
    {
        string quote = await context.CallActivityAsync<string>("ShippingQuote", itemCount);
        return $"Workflow v2 -> {quote}";
    }
}

/// <summary>
/// CheckoutWorkflow v2 - explicitly overrides the inherited activity version.
/// </summary>
[DurableTask("ExplicitOverrideCheckoutWorkflow")]
[DurableTaskVersion("2")]
public sealed class ExplicitOverrideCheckoutWorkflowV2 : TaskOrchestrator<int, string>
{
    /// <inheritdoc />
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int itemCount)
    {
        string quote = await context.CallShippingQuote_1Async(itemCount);
        return $"Workflow v2 explicit override -> {quote}";
    }
}

/// <summary>
/// ShippingQuote v1 - uses a flat shipping charge.
/// </summary>
[DurableTask("ShippingQuote")]
[DurableTaskVersion("1")]
public sealed class ShippingQuoteV1 : TaskActivity<int, string>
{
    /// <inheritdoc />
    public override Task<string> RunAsync(TaskActivityContext context, int itemCount)
    {
        int total = (itemCount * 10) + 7;
        return Task.FromResult($"activity v1 quote: ${total} (flat $7 shipping)");
    }
}

/// <summary>
/// ShippingQuote v2 - applies a bulk discount and cheaper shipping.
/// </summary>
[DurableTask("ShippingQuote")]
[DurableTaskVersion("2")]
public sealed class ShippingQuoteV2 : TaskActivity<int, string>
{
    /// <inheritdoc />
    public override Task<string> RunAsync(TaskActivityContext context, int itemCount)
    {
        int total = (itemCount * 10) + 5;
        if (itemCount >= 5)
        {
            total -= 10;
        }

        return Task.FromResult($"activity v2 quote: ${total} ($10 bulk discount + $5 shipping)");
    }
}
