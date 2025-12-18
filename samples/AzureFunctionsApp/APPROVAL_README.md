# Approval Orchestrator Sample

This sample demonstrates the use of strongly-typed external events with Azure Functions using the `DurableEventAttribute`.

## Overview

The Approval Orchestrator showcases how to:
1. Define an event type with `[DurableEvent]` attribute
2. Use the generated strongly-typed `WaitFor{EventName}Async` method
3. Raise events from HTTP triggers

## Files

- **ApprovalOrchestrator.cs**: Contains the orchestrator, activity, and event definitions

## Event Definition

```csharp
[DurableEvent(nameof(ApprovalEvent))]
public sealed record ApprovalEvent(bool Approved, string? Approver);
```

The source generator automatically creates:
```csharp
public static Task<ApprovalEvent> WaitForApprovalEventAsync(
    this TaskOrchestrationContext context, 
    CancellationToken cancellationToken = default)
```

## Usage in Orchestrator

```csharp
[DurableTask(nameof(ApprovalOrchestrator))]
public class ApprovalOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string requestName)
    {
        // Send notification
        await context.CallNotifyApprovalRequiredAsync(requestName);
        
        // Wait for approval using strongly-typed method
        ApprovalEvent approvalEvent = await context.WaitForApprovalEventAsync();
        
        if (approvalEvent.Approved)
        {
            return $"Request '{requestName}' was approved by {approvalEvent.Approver}";
        }
        else
        {
            return $"Request '{requestName}' was rejected by {approvalEvent.Approver}";
        }
    }
}
```

## Testing the Sample

1. Start the orchestration:
   ```bash
   curl -X POST http://localhost:7071/api/StartApprovalOrchestrator \
     -H "Content-Type: text/plain" \
     -d "My Important Request"
   ```

2. Send an approval event:
   ```bash
   curl -X POST "http://localhost:7071/api/approval/{instanceId}?approve=true" \
     -H "Content-Type: text/plain" \
     -d "John Doe"
   ```

   Or reject:
   ```bash
   curl -X POST "http://localhost:7071/api/approval/{instanceId}?approve=false" \
     -H "Content-Type: text/plain" \
     -d "Jane Smith"
   ```

3. Check the orchestration status using the links returned from the start request.

## Benefits

- **Type Safety**: Compile-time checking of event payloads
- **IntelliSense**: IDE support for discovering available event methods
- **Less Boilerplate**: No need for string literals and explicit generic types
- **Refactoring Support**: Renaming event types updates generated code automatically
