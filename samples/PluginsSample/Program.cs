// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates the Durable Task Plugin system, which is inspired by Temporal's
// plugin/interceptor pattern. It shows how to use the 5 built-in plugins:
// 1. LoggingPlugin - Structured logging for orchestration and activity lifecycle events
// 2. MetricsPlugin - Execution counts, durations, and success/failure tracking
// 3. AuthorizationPlugin - Input-based authorization checks before execution
// 4. ValidationPlugin - Input validation before task execution
// 5. RateLimitingPlugin - Token-bucket rate limiting for activity dispatches

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Create a shared metrics store so we can read metrics after the orchestration completes.
MetricsStore metricsStore = new();

// Use the in-process test host (no external sidecar needed for demonstration).
// In production, replace with .UseGrpc() or .UseDurableTaskScheduler().
await using DurableTaskTestHost testHost = await DurableTaskTestHost.StartAsync(
    registry =>
    {
        // Register orchestration and activities.
        registry.AddOrchestratorFunc("GreetingOrchestration", async context =>
        {
            List<string> greetings = new()
            {
                await context.CallActivityAsync<string>("SayHello", "Tokyo"),
                await context.CallActivityAsync<string>("SayHello", "London"),
                await context.CallActivityAsync<string>("SayHello", "Seattle"),
            };

            return greetings;
        });

        registry.AddActivityFunc<string, string>("SayHello", (context, city) =>
        {
            return $"Hello, {city}!";
        });
    });

DurableTaskClient client = testHost.Client;

// Schedule a new orchestration instance.
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("GreetingOrchestration");
Console.WriteLine($"Started orchestration: {instanceId}");

// Wait for the orchestration to complete.
OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);
Console.WriteLine($"Orchestration completed with status: {result.RuntimeStatus}");
Console.WriteLine($"Output: {result.SerializedOutput}");

// --- Demonstrate Plugin APIs ---
Console.WriteLine("\n=== Plugin Demonstrations ===");

// Demo 1: LoggingPlugin
Console.WriteLine("\n--- 1. LoggingPlugin ---");
Console.WriteLine("The LoggingPlugin emits structured ILogger events for lifecycle events.");
Console.WriteLine("It would be registered on a worker builder like this:");
Console.WriteLine("  builder.Services.AddDurableTaskWorker().UseLoggingPlugin().UseGrpc();");

ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
LoggingPlugin loggingPlugin = new(loggerFactory);
Console.WriteLine($"Plugin name: {loggingPlugin.Name}");
Console.WriteLine($"Orchestration interceptors: {loggingPlugin.OrchestrationInterceptors.Count}");
Console.WriteLine($"Activity interceptors: {loggingPlugin.ActivityInterceptors.Count}");

// Demo 2: MetricsPlugin
Console.WriteLine("\n--- 2. MetricsPlugin ---");
MetricsPlugin metricsPlugin = new(metricsStore);

// Simulate lifecycle events
var orchCtx = new Microsoft.DurableTask.Plugins.OrchestrationInterceptorContext("Demo", "test-1", false, null);
await metricsPlugin.OrchestrationInterceptors[0].OnOrchestrationStartingAsync(orchCtx);
await metricsPlugin.OrchestrationInterceptors[0].OnOrchestrationCompletedAsync(orchCtx, "done");

var actCtx = new Microsoft.DurableTask.Plugins.ActivityInterceptorContext("DemoActivity", "test-1", "input");
await metricsPlugin.ActivityInterceptors[0].OnActivityStartingAsync(actCtx);
await metricsPlugin.ActivityInterceptors[0].OnActivityCompletedAsync(actCtx, "result");
await metricsPlugin.ActivityInterceptors[0].OnActivityStartingAsync(actCtx);
await metricsPlugin.ActivityInterceptors[0].OnActivityFailedAsync(actCtx, new Exception("test failure"));

Console.WriteLine("Orchestration metrics:");
foreach (var (name, metrics) in metricsStore.GetAllOrchestrationMetrics())
{
    Console.WriteLine($"  '{name}': Started={metrics.Started}, Completed={metrics.Completed}, Failed={metrics.Failed}");
}

Console.WriteLine("Activity metrics:");
foreach (var (name, metrics) in metricsStore.GetAllActivityMetrics())
{
    Console.WriteLine($"  '{name}': Started={metrics.Started}, Completed={metrics.Completed}, Failed={metrics.Failed}");
}

// Demo 3: AuthorizationPlugin
Console.WriteLine("\n--- 3. AuthorizationPlugin ---");
AuthorizationPlugin authPlugin = new(new AllowAllAuthorizationHandler());
var authOrcCtx = new Microsoft.DurableTask.Plugins.OrchestrationInterceptorContext("SecureOrch", "secure-1", false, null);
await authPlugin.OrchestrationInterceptors[0].OnOrchestrationStartingAsync(authOrcCtx);

// Demo 4: ValidationPlugin
Console.WriteLine("\n--- 4. ValidationPlugin ---");
ValidationPlugin validationPlugin = new(new CityNameValidator());
var validCtx = new Microsoft.DurableTask.Plugins.ActivityInterceptorContext("SayHello", "val-1", "Tokyo");
await validationPlugin.ActivityInterceptors[0].OnActivityStartingAsync(validCtx);
Console.WriteLine("  Validation passed for input 'Tokyo'");

try
{
    var invalidCtx = new Microsoft.DurableTask.Plugins.ActivityInterceptorContext("SayHello", "val-1", "");
    await validationPlugin.ActivityInterceptors[0].OnActivityStartingAsync(invalidCtx);
}
catch (ArgumentException ex)
{
    Console.WriteLine($"  Validation correctly rejected empty input: {ex.Message}");
}

// Demo 5: RateLimitingPlugin
Console.WriteLine("\n--- 5. RateLimitingPlugin ---");
RateLimitingPlugin rateLimitPlugin = new(new RateLimitingOptions
{
    MaxTokens = 3,
    RefillRate = 0,
    RefillInterval = TimeSpan.FromHours(1),
});

var rlCtx = new Microsoft.DurableTask.Plugins.ActivityInterceptorContext("LimitedAction", "rl-1", "data");
int allowed = 0;
int denied = 0;
for (int i = 0; i < 5; i++)
{
    try
    {
        await rateLimitPlugin.ActivityInterceptors[0].OnActivityStartingAsync(rlCtx);
        allowed++;
    }
    catch (RateLimitExceededException)
    {
        denied++;
    }
}

Console.WriteLine($"  Rate limit (max 3): Allowed={allowed}, Denied={denied}");

// Demo: SimplePlugin builder with built-in activities
Console.WriteLine("\n--- SimplePlugin Builder (with built-in activities) ---");
Console.WriteLine("Plugins can provide reusable activities that auto-register when added to a worker.");
Console.WriteLine("Example: A 'StringUtils' plugin that ships pre-built string activities.");

// This is how a plugin author would package reusable activities.
// Users just call .UsePlugin(stringUtilsPlugin) and the activities become available.
var stringUtilsPlugin = Microsoft.DurableTask.Plugins.SimplePlugin.NewBuilder("MyOrg.StringUtils")
    .AddTasks(registry =>
    {
        registry.AddActivityFunc<string, string>("StringUtils.ToUpper", (ctx, input) => input.ToUpperInvariant());
        registry.AddActivityFunc<string, string>("StringUtils.Reverse", (ctx, input) =>
            new string(input.Reverse().ToArray()));
        registry.AddActivityFunc<string, int>("StringUtils.WordCount", (ctx, input) =>
            input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    })
    .AddOrchestrationInterceptor(loggingPlugin.OrchestrationInterceptors[0])
    .Build();

// Verify the plugin registers its tasks into a registry
DurableTaskRegistry testRegistry = new();
stringUtilsPlugin.RegisterTasks(testRegistry);
Console.WriteLine($"  Plugin '{stringUtilsPlugin.Name}' registered activities into the worker.");
Console.WriteLine($"  Orchestration interceptors: {stringUtilsPlugin.OrchestrationInterceptors.Count}");
Console.WriteLine("");
Console.WriteLine("  Usage in production:");
Console.WriteLine("    builder.Services.AddDurableTaskWorker()");
Console.WriteLine("        .UsePlugin(stringUtilsPlugin)  // auto-registers StringUtils.* activities");
Console.WriteLine("        .UseGrpc();");
Console.WriteLine("");
Console.WriteLine("  Then in an orchestration:");
Console.WriteLine("    string upper = await context.CallActivityAsync<string>(\"StringUtils.ToUpper\", \"hello\");");

Console.WriteLine("\n=== All plugin demonstrations completed successfully! ===");

// --- Helper classes for the sample ---

/// <summary>
/// A simple authorization handler that allows all tasks to execute.
/// In a real application, this would check user claims, roles, or other policies.
/// </summary>
sealed class AllowAllAuthorizationHandler : IAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(AuthorizationContext context)
    {
        Console.WriteLine($"  [Auth] Authorized {context.TargetType} '{context.Name}' for instance '{context.InstanceId}'");
        return Task.FromResult(true);
    }
}

/// <summary>
/// A validator that ensures city names passed to the SayHello activity are non-empty strings.
/// </summary>
sealed class CityNameValidator : IInputValidator
{
    public Task<ValidationResult> ValidateAsync(TaskName taskName, object? input)
    {
        // Only validate the SayHello activity.
        if (taskName.Name == "SayHello")
        {
            if (input is not string city || string.IsNullOrWhiteSpace(city))
            {
                return Task.FromResult(ValidationResult.Failure("City name must be a non-empty string."));
            }
        }

        return Task.FromResult(ValidationResult.Success);
    }
}

