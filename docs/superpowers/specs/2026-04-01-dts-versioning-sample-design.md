## DTS versioning sample design

### Goal

Add a new sample app under `samples/` that runs against the Durable Task Scheduler (DTS) emulator and demonstrates both versioning approaches supported by this repo:

1. **Worker-level versioning** via `UseVersioning(...)`
2. **Per-orchestrator versioning** via `[DurableTaskVersion]`

### Assumption

The user did not answer the clarification prompt, so this design assumes “both versioning approaches” means the two approaches above. This matches the current repo capabilities and the recently implemented per-orchestration versioning work.

## Approaches considered

### Approach 1 — One console sample with two sequential demos (**recommended**)

Create a single console app that:

1. runs a **worker-level versioning** demo first
2. then runs a **per-orchestrator versioning** demo

Each demo starts its own worker/client host against the same DTS emulator connection string and prints the results to the console.

**Pros**
- Matches the request for a single sample app
- Makes the comparison between the two approaches explicit
- Keeps DTS emulator setup and README instructions simple
- Fits the repo’s existing console-sample patterns

**Cons**
- The sample has more code than a single-focus sample
- The worker-level demo and per-orchestrator demo must be kept visually separated to avoid confusion

### Approach 2 — One console sample with command-line modes

Create one sample app with subcommands such as `worker-level` and `per-orchestrator`.

**Pros**
- Strong separation of concerns
- Easier to explain each path independently

**Cons**
- More ceremony for a sample that should be easy to run
- Users must rerun or pass arguments to see the full story

### Approach 3 — Two separate sample apps

Create one DTS sample for worker-level versioning and another for per-orchestrator versioning.

**Pros**
- Simplest code per sample
- Each sample stays narrowly focused

**Cons**
- Conflicts with the “sample app” request
- Duplicates emulator setup, host wiring, and documentation
- Makes it harder to compare the two approaches side-by-side

## Decision

Use **Approach 1**: one DTS emulator console sample with two sequential demos.

This is the clearest way to teach the contrast:

- **worker-level versioning** is for rolling a single logical implementation per worker run
- **per-orchestrator versioning** is for keeping multiple orchestrator implementations for the same logical name active in one worker process

## Sample structure

### New sample

- `samples/VersioningSample/VersioningSample.csproj`
- `samples/VersioningSample/Program.cs`
- `samples/VersioningSample/README.md`

### Existing files to update

- `Microsoft.DurableTask.sln` — add the new sample project
- `README.md` — add a short DTS sample reference in the Durable Task Scheduler section

## Programming model

### Shared sample shape

The sample will:

- use `HostApplicationBuilder`
- read `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` from configuration
- configure both `AddDurableTaskClient(...UseDurableTaskScheduler(...))` and `AddDurableTaskWorker(...UseDurableTaskScheduler(...))`
- start the host, run the demos, print results, and stop the host

The sample will target `net8.0;net10.0`, matching the newer DTS console sample pattern.

### Demo 1 — Worker-level versioning

This demo will show that worker-level versioning is **host-scoped**, not multi-version-in-one-process.

Design:

1. Start a host configured with:
   - an unversioned orchestration registration for a logical name such as `WorkerLevelGreeting`
   - `UseVersioning(new DurableTaskWorkerOptions.VersioningOptions { Version = "1.0", MatchStrategy = Strict, FailureStrategy = Fail, DefaultVersion = "1.0" })`
2. Schedule and complete an instance using version `1.0`
3. Stop the host
4. Start a second host with the same logical orchestration name but a different implementation and worker version `2.0`
5. Schedule and complete an instance using version `2.0`

The sample output should make the lesson explicit: **worker-level versioning upgrades the worker deployment; it does not keep multiple implementations of the same orchestration active in one worker process**.

Implementation note:

- To avoid class-name and source-generator collisions, this demo should use explicit manual registrations (`AddOrchestratorFunc(...)` or equivalent) rather than multiple same-name unversioned `[DurableTask]` classes in the same project.

### Demo 2 — Per-orchestrator versioning

This demo will show that `[DurableTaskVersion]` allows multiple implementations of the same logical orchestration name to coexist in one worker process.

Design:

1. Define two class-based orchestrators with the same `[DurableTask("OrderWorkflow")]` name and distinct `[DurableTaskVersion("v1")]` / `[DurableTaskVersion("v2")]` values
2. Register them together using generated `AddAllGeneratedTasks()`
3. Start one instance with version `v1`
4. Start another instance with version `v2`
5. Run a small migration example that starts on `v1` and calls `ContinueAsNew(new ContinueAsNewOptions { NewVersion = "v2", ... })`

The sample output should show:

- `v1` routed to the `v1` implementation
- `v2` routed to the `v2` implementation
- `ContinueAsNewOptions.NewVersion` migrating a long-running orchestration at a replay-safe boundary

Implementation note:

- This demo should use class-based syntax and the source generator because `[DurableTaskVersion]` is part of the new feature being taught.

## Code organization

To align with the repo’s sample guidance, the sample should stay compact and readable:

- one `Program.cs`
- top-of-file comments explaining the two demos
- helper methods such as `RunWorkerLevelVersioningDemoAsync(...)` and `RunPerOrchestratorVersioningDemoAsync(...)`
- task and activity classes placed at the bottom of the file

## README content

The sample README should include:

1. What the sample demonstrates
2. The distinction between worker-level and per-orchestrator versioning
3. DTS emulator startup instructions:

   ```bash
   docker run --name durabletask-emulator -d -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dts/dts-emulator:latest
   ```

4. Connection string setup:

   ```bash
   export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
   ```

5. Run instructions:

   ```bash
   dotnet run --project samples/VersioningSample/VersioningSample.csproj
   ```

6. A short explanation of when to choose each versioning approach
7. A note that per-orchestrator `[DurableTaskVersion]` routing should not be combined with worker-level `UseVersioning(...)` in the same worker path because both use the orchestration instance version field

## Error handling and UX

The sample should fail fast when `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` is missing.

Console output should clearly label:

- when the sample is running the worker-level demo
- when it is running the per-orchestrator demo
- which version completed
- why the two approaches are different

## Verification

Implementation verification should include:

1. `dotnet build samples/VersioningSample/VersioningSample.csproj`
2. `dotnet run --project samples/VersioningSample/VersioningSample.csproj` against a running DTS emulator
3. confirmation that the sample prints successful results for:
   - worker-level `1.0`
   - worker-level `2.0`
   - per-orchestrator `v1`
   - per-orchestrator `v2`
   - per-orchestrator migration `v1 -> v2`

## Scope boundaries

This sample will **not**:

- attempt to demonstrate Azure Functions multi-version routing
- add automated sample tests
- demonstrate every version-match strategy
- mix worker-level versioning and per-orchestrator versioning inside the same running worker path

The sample is educational, not exhaustive.
