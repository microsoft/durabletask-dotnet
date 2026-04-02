# DTS Versioning Sample Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new DTS emulator console sample that demonstrates worker-level versioning and per-orchestrator `[DurableTaskVersion]` routing in one runnable app.

**Architecture:** Create a single `samples/VersioningSample` console app with two sequential demos. The first demo uses manual orchestration registration plus `UseVersioning(...)` to show worker-scoped versioning; the second demo uses class-based orchestrators plus `[DurableTaskVersion]` and generated registration to show same-name multi-version routing and `ContinueAsNewOptions.NewVersion` migration.

**Tech Stack:** .NET console app, `HostApplicationBuilder`, `Microsoft.DurableTask.Client.AzureManaged`, `Microsoft.DurableTask.Worker.AzureManaged`, `Microsoft.DurableTask.Generators`, DTS emulator via `DURABLE_TASK_SCHEDULER_CONNECTION_STRING`

---

### File map

- Create: `samples/VersioningSample/VersioningSample.csproj` — sample project definition
- Create: `samples/VersioningSample/Program.cs` — both demos, helper methods, and sample task types
- Create: `samples/VersioningSample/README.md` — emulator setup, run instructions, and explanation of both approaches
- Modify: `Microsoft.DurableTask.sln` — include the new sample project
- Modify: `README.md` — add a short reference to the new DTS sample in the Durable Task Scheduler section

### Task 1: Scaffold the sample project and implement the worker-level versioning demo

**Files:**
- Create: `samples/VersioningSample/VersioningSample.csproj`
- Create: `samples/VersioningSample/Program.cs`

- [ ] **Step 1: Write the failing sample shell**

```xml
<!-- samples/VersioningSample/VersioningSample.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="$(SrcRoot)Client/AzureManaged/Client.AzureManaged.csproj" />
    <ProjectReference Include="$(SrcRoot)Worker/AzureManaged/Worker.AzureManaged.csproj" />
    <ProjectReference Include="$(SrcRoot)Analyzers/Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="$(SrcRoot)Generators/Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

```csharp
// samples/VersioningSample/Program.cs
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

await RunWorkerLevelVersioningDemoAsync(builder);
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build samples/VersioningSample/VersioningSample.csproj --nologo --verbosity minimal`
Expected: FAIL with a compile error for `RunWorkerLevelVersioningDemoAsync`

- [ ] **Step 3: Write the minimal worker-level demo implementation**

```csharp
// samples/VersioningSample/Program.cs
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates the two versioning models supported by durabletask-dotnet
// when connected directly to the Durable Task Scheduler (DTS) emulator.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

string schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_CONNECTION_STRING is not set.");

await RunWorkerLevelVersioningDemoAsync(schedulerConnectionString);

static async Task RunWorkerLevelVersioningDemoAsync(string schedulerConnectionString)
{
    Console.WriteLine("=== Worker-level versioning ===");

    string v1Result = await RunWorkerScopedVersionAsync(
        schedulerConnectionString,
        workerVersion: "1.0",
        outputPrefix: "worker-v1");
    Console.WriteLine($"Worker version 1.0 completed with output: {v1Result}");

    string v2Result = await RunWorkerScopedVersionAsync(
        schedulerConnectionString,
        workerVersion: "2.0",
        outputPrefix: "worker-v2");
    Console.WriteLine($"Worker version 2.0 completed with output: {v2Result}");

    Console.WriteLine("Worker-level versioning keeps one implementation active per worker run.");
}

static async Task<string> RunWorkerScopedVersionAsync(
    string schedulerConnectionString,
    string workerVersion,
    string outputPrefix)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Services.AddDurableTaskClient(clientBuilder =>
    {
        clientBuilder.UseDurableTaskScheduler(schedulerConnectionString);
        clientBuilder.UseDefaultVersion(workerVersion);
    });

    builder.Services.AddDurableTaskWorker(workerBuilder =>
    {
        workerBuilder.AddTasks(tasks =>
        {
            tasks.AddOrchestratorFunc<string, string>("WorkerLevelGreeting", (context, input) =>
                Task.FromResult($"{outputPrefix}:{context.Version}:{input}"));
        });
        workerBuilder.UseDurableTaskScheduler(schedulerConnectionString);
        workerBuilder.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
        {
            Version = workerVersion,
            DefaultVersion = workerVersion,
            MatchStrategy = DurableTaskWorkerOptions.VersionMatchStrategy.Strict,
            FailureStrategy = DurableTaskWorkerOptions.VersionFailureStrategy.Fail,
        });
    });

    IHost host = builder.Build();
    await host.StartAsync();

    DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
    string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
        "WorkerLevelGreeting",
        input: "hello",
        new StartOrchestrationOptions { Version = workerVersion });
    OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);
    string output = metadata.ReadOutputAs<string>()!;

    await host.StopAsync();
    return output;
}
```

- [ ] **Step 4: Run build to verify the worker-level demo compiles**

Run: `dotnet build samples/VersioningSample/VersioningSample.csproj --nologo --verbosity minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add samples/VersioningSample/VersioningSample.csproj samples/VersioningSample/Program.cs
git commit -m "feat: add DTS worker versioning sample skeleton"
```

### Task 2: Add the per-orchestrator versioning demo to the same sample

**Files:**
- Modify: `samples/VersioningSample/Program.cs`

- [ ] **Step 1: Write the failing per-orchestrator demo calls**

```csharp
// Insert below the worker-level demo call in Program.cs
await RunPerOrchestratorVersioningDemoAsync(schedulerConnectionString);

// Insert below RunWorkerLevelVersioningDemoAsync
static async Task RunPerOrchestratorVersioningDemoAsync(string schedulerConnectionString)
{
    using IHost host = BuildPerOrchestratorHost(schedulerConnectionString);
    await host.StartAsync();

    DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();
    string v1InstanceId = await client.ScheduleNewOrderWorkflow_v1InstanceAsync(5);
    string v2InstanceId = await client.ScheduleNewOrderWorkflow_v2InstanceAsync(5);
    string migrationInstanceId = await client.ScheduleNewMigratingOrderWorkflow_v1InstanceAsync(4);
}
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build samples/VersioningSample/VersioningSample.csproj --nologo --verbosity minimal`
Expected: FAIL because `BuildPerOrchestratorHost`, `OrderWorkflow` types, and generated helper methods do not exist yet

- [ ] **Step 3: Write the per-orchestrator versioning implementation**

```csharp
// Add to samples/VersioningSample/Program.cs
using Microsoft.DurableTask.Client;

await RunPerOrchestratorVersioningDemoAsync(schedulerConnectionString);

static async Task RunPerOrchestratorVersioningDemoAsync(string schedulerConnectionString)
{
    Console.WriteLine("=== Per-orchestrator versioning ===");

    using IHost host = BuildPerOrchestratorHost(schedulerConnectionString);
    await host.StartAsync();

    DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

    string v1InstanceId = await client.ScheduleNewOrderWorkflow_v1InstanceAsync(5);
    OrchestrationMetadata v1 = await client.WaitForInstanceCompletionAsync(v1InstanceId, getInputsAndOutputs: true);
    Console.WriteLine($"OrderWorkflow v1 output: {v1.ReadOutputAs<string>()}");

    string v2InstanceId = await client.ScheduleNewOrderWorkflow_v2InstanceAsync(5);
    OrchestrationMetadata v2 = await client.WaitForInstanceCompletionAsync(v2InstanceId, getInputsAndOutputs: true);
    Console.WriteLine($"OrderWorkflow v2 output: {v2.ReadOutputAs<string>()}");

    string migrationInstanceId = await client.ScheduleNewMigratingOrderWorkflow_v1InstanceAsync(4);
    OrchestrationMetadata migration = await client.WaitForInstanceCompletionAsync(migrationInstanceId, getInputsAndOutputs: true);
    Console.WriteLine($"Migrating workflow output: {migration.ReadOutputAs<string>()}");

    await host.StopAsync();
}

static IHost BuildPerOrchestratorHost(string schedulerConnectionString)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Services.AddDurableTaskClient(clientBuilder => clientBuilder.UseDurableTaskScheduler(schedulerConnectionString));
    builder.Services.AddDurableTaskWorker(workerBuilder =>
    {
        workerBuilder.AddTasks(tasks => tasks.AddAllGeneratedTasks());
        workerBuilder.UseDurableTaskScheduler(schedulerConnectionString);
    });

    return builder.Build();
}

[DurableTask("OrderWorkflow")]
[DurableTaskVersion("v1")]
public sealed class OrderWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
        => Task.FromResult($"v1:{input}");
}

[DurableTask("OrderWorkflow")]
[DurableTaskVersion("v2")]
public sealed class OrderWorkflowV2 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
        => Task.FromResult($"v2:{input}");
}

[DurableTask("MigratingOrderWorkflow")]
[DurableTaskVersion("v1")]
public sealed class MigratingOrderWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
    {
        context.ContinueAsNew(new ContinueAsNewOptions
        {
            NewInput = input + 1,
            NewVersion = "v2",
        });

        return Task.FromResult(string.Empty);
    }
}

[DurableTask("MigratingOrderWorkflow")]
[DurableTaskVersion("v2")]
public sealed class MigratingOrderWorkflowV2 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
        => Task.FromResult($"v2:{input}");
}
```

- [ ] **Step 4: Run build to verify the full sample compiles**

Run: `dotnet build samples/VersioningSample/VersioningSample.csproj --nologo --verbosity minimal`
Expected: PASS

- [ ] **Step 5: Run against the DTS emulator**

Run: `dotnet run --project samples/VersioningSample/VersioningSample.csproj`
Expected: output includes:
- `Worker version 1.0 completed with output: worker-v1:1.0:hello`
- `Worker version 2.0 completed with output: worker-v2:2.0:hello`
- `OrderWorkflow v1 output: v1:5`
- `OrderWorkflow v2 output: v2:5`
- `Migrating workflow output: v2:5`

- [ ] **Step 6: Commit**

```bash
git add samples/VersioningSample/Program.cs
git commit -m "feat: demonstrate per-orchestrator DTS versioning"
```

### Task 3: Document the sample and wire it into the repo

**Files:**
- Create: `samples/VersioningSample/README.md`
- Modify: `Microsoft.DurableTask.sln`
- Modify: `README.md`

- [ ] **Step 1: Write the sample README**

````md
# DTS Versioning Sample

This sample demonstrates the two versioning models available when you run durabletask-dotnet directly against the Durable Task Scheduler (DTS) emulator:

1. **Worker-level versioning** via `UseVersioning(...)`
2. **Per-orchestrator versioning** via `[DurableTaskVersion]`

## Run the DTS emulator

```bash
docker run --name durabletask-emulator -d -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 mcr.microsoft.com/dts/dts-emulator:latest
```

## Configure the connection string

```bash
export DURABLE_TASK_SCHEDULER_CONNECTION_STRING="Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
```

## Run the sample

```bash
dotnet run --project samples/VersioningSample/VersioningSample.csproj
```

## What to look for

- The worker-level demo runs one implementation per worker version (`1.0`, then `2.0`)
- The per-orchestrator demo keeps `v1` and `v2` of the same logical orchestration active in one worker process
- The migration demo uses `ContinueAsNewOptions.NewVersion` to move from `v1` to `v2`

> Do not combine `[DurableTaskVersion]` routing with worker-level `UseVersioning(...)` in the same worker path. Both features use the orchestration instance version field.
````

- [ ] **Step 2: Add the sample to the solution**

Run:

```bash
dotnet sln Microsoft.DurableTask.sln add samples/VersioningSample/VersioningSample.csproj
```

Expected: `Project 'samples/VersioningSample/VersioningSample.csproj' added to the solution.`

- [ ] **Step 3: Add a short root README reference**

```md
<!-- Insert in README.md under "Usage with the Durable Task Scheduler" -->

For a runnable DTS emulator example that compares worker-level versioning with per-orchestrator `[DurableTaskVersion]` routing, see [samples/VersioningSample](samples/VersioningSample/README.md).
```

- [ ] **Step 4: Run final sample build verification**

Run: `dotnet build samples/VersioningSample/VersioningSample.csproj --nologo --verbosity minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add samples/VersioningSample/README.md Microsoft.DurableTask.sln README.md
git commit -m "docs: add DTS versioning sample"
```

### Task 4: Final verification

**Files:**
- Verify only; no new files

- [ ] **Step 1: Run the focused sample verification**

Run:

```bash
dotnet build samples/VersioningSample/VersioningSample.csproj --nologo --verbosity minimal && \
dotnet run --project samples/VersioningSample/VersioningSample.csproj
```

Expected:
- build succeeds
- console output shows both worker-level and per-orchestrator demo results

- [ ] **Step 2: Run impacted versioning coverage**

Run:

```bash
dotnet test test/Worker/Core.Tests/Worker.Tests.csproj --filter "DurableTaskFactoryVersioningTests|UseWorkItemFiltersTests" --nologo --verbosity minimal && \
dotnet test test/Generators.Tests/Generators.Tests.csproj --filter "VersionedOrchestratorTests|AzureFunctionsTests" --nologo --verbosity minimal && \
dotnet test test/Grpc.IntegrationTests/Grpc.IntegrationTests.csproj --filter "VersionedClassSyntaxIntegrationTests|OrchestrationVersionPassedThroughContext|OrchestrationVersioning_MatchTypeNotSpecified_NoVersionFailure|OrchestrationVersioning_MatchTypeNone_NoVersionFailure|OrchestrationVersioning_MatchTypeCurrentOrOlder_VersionSuccess|SubOrchestrationInheritsDefaultVersion|OrchestrationTaskVersionOverridesDefaultVersion|SubOrchestrationTaskVersionOverridesDefaultVersion|ContinueAsNewWithNewVersion" --nologo --verbosity minimal
```

Expected: PASS across all targeted worker, generator, and gRPC integration tests

- [ ] **Step 3: Commit any verification-only adjustments**

```bash
git add -A
git commit -m "chore: finalize DTS versioning sample"
```
