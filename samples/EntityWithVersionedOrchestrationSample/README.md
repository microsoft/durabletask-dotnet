# Entity-backed Orchestration Migration (v1 → v2 mid-life)

This sample shows the **one** scenario where per-orchestrator `[DurableTask(Version = "...")]` and entities genuinely compose into something neither feature can do alone: a single long-running orchestration instance migrates from v1 to v2 mid-flight via `ContinueAsNew(NewVersion = "2")`, and the entity state it has been writing to **survives the version transition**.

## How this differs from the other versioning samples

| Sample | What it shows |
|---|---|
| `PerOrchestratorVersioningSample` | Two **parallel** instances at different versions, each running independently. |
| `ActivityVersioningSample` | A versioned orchestration calling versioned activities. |
| `WorkerVersioningSample` | Worker-level deployment versioning via `UseVersioning()`. |
| **This sample** | **One** orchestration instance whose logic version changes mid-life while external state held by an entity is preserved. |

## What it shows

- `JobLog` is an unversioned `[DurableTask]` `TaskEntity<int>` that tracks the count of processed jobs.
- `ProcessJobsWorkflow` has two versions:
  - **v1 (original)**: processes one job per cycle (the bug — should be two), records it on `JobLog`, then `ContinueAsNew(NewVersion = "2")` to apply the fix.
  - **v2 (fixed)**: reads `JobLog`, sees v1's earlier contribution, processes the remaining jobs in one batch, completes.
- The **same instance ID** runs through both versions. The `JobLog` count incremented by v1 is visible to v2.

In production, this is the pattern for fixing a bug in an eternal orchestration: you can't terminate it (it's holding distributed state), and you can't lose its progress. `ContinueAsNew(NewVersion = ...)` lets you swap the logic at a deterministic boundary, and the entity holds the state you can't afford to drop.

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

Expected output (instance ID will vary):

```text
=== Entity-backed orchestration migrating from v1 to v2 mid-life ===

Starting ProcessJobsWorkflow at version v1 ...
  Instance ID: 6c2a4b9e8c4d4f1ab39a2dde1c8f7e10
  Final status:  Completed
  Final output:  v2 saw 1 jobs already processed by v1; processed 2 more; total now 3

JobLog (queried directly): 3 jobs recorded total

Done! A single orchestration instance transitioned from v1 to v2 mid-flight.
The JobLog entity preserved the count contributed by v1 across the version change.
```

Observe:

- The orchestration was **scheduled at v1** but the final output is produced by **v2** — the same instance migrated mid-flight via `ContinueAsNew(NewVersion = "2")`.
- The output reports `v2 saw 1 jobs already processed by v1` — proof that the entity state survived the boundary.
- The final `JobLog` count (3) is the sum of v1's contribution (1 job) and v2's contribution (2 jobs) into the same entity.

### 4. Clean up

```bash
docker rm -f durabletask-emulator
```

## Key takeaways

- **Entities are the right place to anchor state you can't lose during a version migration.** Local orchestration variables don't survive `ContinueAsNew`; entity state does.
- **`ContinueAsNew(NewVersion = "...")` is the safe migration boundary** for eternal orchestrations. The history is fully reset, so there's no replay-determinism risk from changing logic. External state (entities, activities, sub-orchestrations completed before the boundary) persists.
- **Entities themselves stay unversioned by design.** A single entity identity is the source of truth for some piece of state; versioning the identity would fork it.

## Lifecycle of V1: how the sample's V1 came to be

Reading the sample, `ProcessJobsWorkflowV1` looks like it already knows about V2 (it calls `ContinueAsNew(NewVersion = "2")`). That's not how V1 starts life. The realistic timeline:

### Day 1 — original V1 is shipped

V1 is an eternal loop. Its last line is `ContinueAsNew()` (no version arg). It has no knowledge that V2 will ever exist.

```csharp
// What the *original* V1 looked like before V2 existed:
[DurableTask("ProcessJobsWorkflow", Version = "1")]
public sealed class ProcessJobsWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int totalJobs)
    {
        EntityInstanceId logId = new(nameof(JobLog), "production-job-log");
        // ... process work, record on JobLog ...
        context.ContinueAsNew(totalJobs);  // loop on V1
        return Task.FromResult(string.Empty);
    }
}
```

### Day 30 — bug discovered, V2 written

You add `ProcessJobsWorkflowV2` as a brand-new class with the fix. Both classes now coexist in the registry.

### Day 30 — V1 gets a surgical hotfix to migrate its in-flight instances

You change one line in V1: replace `context.ContinueAsNew(totalJobs)` with `context.ContinueAsNew(new ContinueAsNewOptions { NewVersion = "2", NewInput = totalJobs })`. **That's the only change.** Everything before that line is unchanged. This is the V1 the sample shows.

### Day 31+ — in-flight V1 instances migrate themselves at their next turn boundary

Each turn ends with `ContinueAsNew(NewVersion = "2")`, restarting the same instance ID as a V2 instance. Entity state (held in `JobLog`) survives the boundary because entities are independent of orchestration lifecycle.

### Day 60 — V1 class can be removed

Once no V1 instances remain (you can verify via the management API or by querying the version field), delete the V1 class. From this point on, only V2 (and any future V3) live in the registry.

### Is the Day-30 surgical hotfix replay-safe?

Yes. `ContinueAsNew` is the **last action** of a turn. Adding `NewVersion = "2"` to an existing `ContinueAsNew(...)` call doesn't add a new action or change the order of any prior actions in the turn — it only changes the post-turn restart parameters the runtime uses for the next execution.

As long as everything in V1 before the `ContinueAsNew` line is unchanged, replay of any in-flight V1 instance succeeds: history records the prior actions, the redeployed code emits the same prior actions, and the final `ContinueAsNew` is treated as a complete-the-turn action whose arguments inform how the next execution starts (not what gets compared against history).

The single-line edit rule matters: if your hotfix also tweaks the loop body (an extra entity call, a removed timer, reordered operations), that *is* a non-deterministic change for instances mid-turn at deploy time. Keep the hotfix surgical — only the `ContinueAsNew` arguments change.

## What if I add v3 tomorrow? (Replay-determinism reference)

Three scenarios are worth pulling apart, because the answer is different for each:

### 1. Inside the v1→v2 migration itself

`ContinueAsNew` **fully resets the history**. The post-`ContinueAsNew` v2 turn starts with an empty history; there's nothing for v2's code to "disagree with". Replay determinism applies *within* a single execution, and `ContinueAsNew` ends the current execution and starts a new one. So the migration itself doesn't break determinism — the v2 instance is a fresh execution that happens to share the same instance ID.

### 2. You add v3 tomorrow as a new class — safe and additive

Adding `[DurableTask("ProcessJobsWorkflow", Version = "3")]` as a brand-new class is non-breaking. The dispatch rule keys on `(name, version)`:

- The v2 instance from the sample is tagged with version `"2"` on the wire. The worker still routes it to your unchanged `ProcessJobsWorkflowV2` class. v3 is invisible to it.
- New `ScheduleNewProcessJobsWorkflowV3InstanceAsync(...)` calls start fresh v3 instances; they have no shared history with v2 instances.
- A v2 instance can opt to migrate to v3 the same way v1 migrated to v2: `ContinueAsNew(NewVersion = "3")`. Same fresh-history guarantee.

This is exactly the workflow per-task versioning is designed for: ship v3 alongside v2, drain or migrate v2 instances at your own pace.

### 3. You modify the existing `ProcessJobsWorkflowV2` class in place — DON'T

This is the only scenario that breaks determinism, and it isn't specific to `ContinueAsNew` or per-task versioning — it's the universal "don't change a shipped orchestrator's code while instances are mid-execution" rule.

If a v2 instance has accumulated history (called the entity, awaited a timer, etc.) and you change `ProcessJobsWorkflowV2`'s code such that the next replay emits different actions than the history shows, you get a non-determinism failure. The mitigation is exactly what versioning enables: don't edit `V2`. Add `V3`. Migrate via `ContinueAsNew(NewVersion = "3")` at a deterministic boundary (which, by definition, is *between* turns, with a clean history reset).

### The mental model

The class you ship is your contract: `[DurableTask("X", Version = "v2")]` says "this class **is** the v2 implementation forever." When the logic needs to change, ship a new class with `Version = "v3"` and migrate at a `ContinueAsNew` boundary. Per-task versioning is what makes this *practical*, because you can hold multiple versions in one worker instead of spinning up a separate deployment.

## See also

- [PerOrchestratorVersioningSample](../PerOrchestratorVersioningSample/README.md) — multi-version orchestration without entities; also includes a `MigratingWorkflow` example that uses `ContinueAsNew(NewVersion = "...")` without entities.
- [ActivityVersioningSample](../ActivityVersioningSample/README.md) — versioning across activities, including explicit override of the inherited orchestration version.
- [WorkerVersioningSample](../WorkerVersioningSample/README.md) — worker-level (deployment) versioning via `UseVersioning()`.
