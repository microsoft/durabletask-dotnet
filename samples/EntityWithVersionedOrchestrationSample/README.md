# Entity + Per-Orchestrator Versioning Sample

This sample shows how durable entities compose with per-orchestrator `[DurableTask(Version = "...")]` versioning. **Entities are intentionally unversioned**: a single entity identity persists across every orchestrator version that touches it. A v1 orchestration and a v2 orchestration can write to the same entity, and that entity's state is shared between them.

## What it shows

- `WalletEntity` is a single, unversioned `[DurableTask] TaskEntity<int>` holding a balance.
- `CheckoutWorkflow` has two versions:
  - **v1** deducts the purchase price directly.
  - **v2** applies a 10% loyalty discount, then deducts.
- Both versions call the **same** wallet (`"wallet-42"`). State accumulated by v1 is visible to v2.
- `AddAllGeneratedTasks()` registers the entity and both orchestrator versions in one call.

## Prerequisites

- .NET 8.0 or 10.0 SDK
- [Docker](https://www.docker.com/get-started)

## Running the Sample

### 1. Start the DTS emulator

```bash
docker run --name durabletask-emulator -d -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dts/dts-emulator:latest
```

### 2. Set the connection string

```bash
export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
```

### 3. Run the sample

```bash
dotnet run
```

Expected output:

```text
=== Entities + per-orchestrator versioning ===

Seeded wallet 'wallet-42' with $100.

Scheduling CheckoutWorkflow v1 for $30 (no discount) ...
  Result: v1 charged $30; new balance $70

Scheduling CheckoutWorkflow v2 for $30 (10% loyalty discount applied) ...
  Result: v2 charged $27 (was $30, 10% discount); new balance $43

Final wallet balance (queried directly): $43

Done! The unversioned WalletEntity persisted state across both orchestration versions.
```

### 4. Clean up

```bash
docker rm -f durabletask-emulator
```

## How versioning composes with entities

| Layer | Versioned? | Why |
|---|---|---|
| Orchestrator | Yes — `[DurableTask("CheckoutWorkflow", Version = "1")]` / `Version = "2"` | Logic evolves; multiple revisions may need to coexist while in-flight instances complete. |
| Activity | Yes (optional) | Same reason as orchestrator. |
| **Entity** | **No** | Entities represent a single source of truth for some piece of state. Versioning the identity would fork the state and silently break readers/writers. The proposal leaves entities unversioned by design. |

A versioned orchestrator can call or signal an entity exactly as before:

```csharp
[DurableTask("CheckoutWorkflow", Version = "2")]
public sealed class CheckoutWorkflowV2 : TaskOrchestrator<int, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, int price)
    {
        EntityInstanceId walletId = new(nameof(WalletEntity), "wallet-42");
        int newBalance = await context.Entities.CallEntityAsync<int>(walletId, "Withdraw", price);
        return $"new balance ${newBalance}";
    }
}
```

The `[DurableTask]` `Version` argument is ignored on `TaskEntity<TState>` subclasses; declaring it produces a warning at compile time (orchestrator/activity behavior only).

## See also

- [PerOrchestratorVersioningSample](../PerOrchestratorVersioningSample/README.md) — multi-version orchestration without entities.
- [ActivityVersioningSample](../ActivityVersioningSample/README.md) — versioning across activities, including explicit override of the inherited orchestration version.
- [WorkerVersioningSample](../WorkerVersioningSample/README.md) — worker-level (deployment) versioning via `UseVersioning()`.
