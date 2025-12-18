# Strongly-Typed Events Sample

This sample demonstrates the use of strongly-typed external events using the `DurableEventAttribute`.

## Overview

The `DurableEventAttribute` allows you to define event types that automatically generate strongly-typed extension methods for waiting on external events in orchestrations. This provides compile-time type safety and better IntelliSense support.

## Key Features

1. **Strongly-Typed Event Definitions**: Define event types using records or classes with the `[DurableEvent]` attribute
2. **Generated Extension Methods**: The source generator automatically creates `WaitFor{EventName}Async` methods
3. **Type Safety**: Event payloads are strongly-typed, reducing runtime errors

## Sample Code

### Defining an Event

```csharp
[DurableEvent(nameof(ApprovalEvent))]
public sealed record ApprovalEvent(bool Approved, string? Approver);
```

This generates an extension method:

```csharp
public static Task<ApprovalEvent> WaitForApprovalEventAsync(
    this TaskOrchestrationContext context, 
    CancellationToken cancellationToken = default);
```

### Using the Generated Method in an Orchestrator

```csharp
[DurableTask("ApprovalOrchestrator")]
public class ApprovalOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string requestName)
    {
        // Wait for approval event using the generated strongly-typed method
        ApprovalEvent approvalEvent = await context.WaitForApprovalEventAsync();
        
        if (approvalEvent.Approved)
        {
            return $"Request approved by {approvalEvent.Approver}";
        }
        else
        {
            return $"Request rejected by {approvalEvent.Approver}";
        }
    }
}
```

### Raising Events from Client Code

```csharp
await client.RaiseEventAsync(
    instanceId, 
    "ApprovalEvent", 
    new ApprovalEvent(true, "John Doe"));
```

## Running the Sample

This sample is configured to use **Durable Task Scheduler (DTS)** (no local gRPC sidecar required).

1. Set the DTS connection string:
    ```bash
    export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="..."
    ```
2. Run the sample:
    ```bash
    dotnet run
    ```

The sample will:
1. Start an approval workflow and wait for an approval event
2. Raise an approval event from the client
3. Complete the workflow with the approval result
4. Start a data processing workflow and demonstrate another event type

## Benefits

- **Type Safety**: Compile-time checking of event payloads
- **IntelliSense**: Better IDE support for discovering available event methods
- **Less Boilerplate**: No need to manually call `WaitForExternalEvent<T>` with string literals
- **Refactoring Support**: Renaming event types automatically updates generated code
