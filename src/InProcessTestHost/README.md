# DurableTaskTestHost - Testing Durable Orchestrations In-Process

`DurableTaskTestHost` is a simple API for testing your durable task orchestrations and activities **in-process** without requiring any external backend.

Supports both **class-based** and **function-based** syntax.

## Quick Start

1. Configure options
```csharp
var options = new DurableTaskTestHostOptions
{
    Port = 31000,                  // Optional: specific port (random by default)
    LoggerFactory = myLoggerFactory // Optional: pass logger factory for logging
};

```

2. Register test orchestrations and activities.

```csharp
await using var testHost = await DurableTaskTestHost.StartAsync(registry =>
{
    // Class-based
    registry.AddOrchestrator<MyOrchestrator>();
    registry.AddActivity<MyActivity>();
    
    // Function-based
    registry.AddOrchestratorFunc("MyFunc", (ctx, input) => Task.FromResult("done"));
    registry.AddActivityFunc("MyActivity", (ctx, input) => Task.FromResult("result"));
});

```

3. Test
```csharp
string instanceId = await testHost.Client.ScheduleNewOrchestrationInstanceAsync("MyOrchestrator");
var result = await testHost.Client.WaitForInstanceCompletionAsync(instanceId);
```
 .
## More Samples

See [DurableTaskTestHostSamples.cs](./DurableTaskTestHostSamples.cs) for complete samples.
