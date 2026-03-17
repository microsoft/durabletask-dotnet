# Namespace Generation Sample

This sample demonstrates how the DurableTask source generator places extension methods into the same namespace as the orchestrator/activity classes.

## What it shows

When using the `[DurableTask]` attribute on classes in different namespaces, the source generator will:

1. Place extension methods (e.g., `ScheduleNewApprovalOrchestratorInstanceAsync()`, `CallRegistrationActivityAsync()`) into the **same namespace** as the task class
2. Keep the `AddAllGeneratedTasks()` registration method in the `Microsoft.DurableTask` namespace
3. Simplify type names within the same namespace (e.g., `MyClass` instead of `MyNS.MyClass`)

This results in cleaner IDE suggestions — you only see extension methods for tasks that are imported via `using` statements.

## Project structure

- `Tasks.cs` - Defines an orchestrator in `NamespaceGenerationSample.Approvals` and an activity in `NamespaceGenerationSample.Registrations`
- `Program.cs` - Shows how to use the generated extension methods with explicit `using` statements

## How to run

1. Start the DTS emulator:
   ```bash
   docker run --name durabletask-emulator -d -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dts/dts-emulator:latest
   ```

2. Set the connection string environment variable:
   ```bash
   export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
   ```

3. Run the sample:
   ```bash
   dotnet run
   ```

## Generated code

The source generator produces code like this:

```csharp
// Extension methods in the task's namespace
namespace NamespaceGenerationSample.Approvals
{
    public static class GeneratedDurableTaskExtensions
    {
        public static Task<string> ScheduleNewApprovalOrchestratorInstanceAsync(
            this IOrchestrationSubmitter client, string input, StartOrchestrationOptions? options = null) { ... }
        
        public static Task<string> CallApprovalOrchestratorAsync(
            this TaskOrchestrationContext context, string input, TaskOptions? options = null) { ... }
    }
}

namespace NamespaceGenerationSample.Registrations
{
    public static class GeneratedDurableTaskExtensions
    {
        public static Task<string> CallRegistrationActivityAsync(
            this TaskOrchestrationContext ctx, int input, TaskOptions? options = null) { ... }
    }
}

// Registration method stays in Microsoft.DurableTask
namespace Microsoft.DurableTask
{
    public static class GeneratedDurableTaskExtensions
    {
        internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder) { ... }
    }
}
```
