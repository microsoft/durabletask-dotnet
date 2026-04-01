// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// =================================================================================================
// Large Payload Externalization Sample
// =================================================================================================
// Demonstrates Azure Blob Storage externalization for oversized payloads.
// Requires: Azurite (azurite --skipApiVersionCheck) + DTS Scheduler connection string.
//
// Run:
//   $env:DURABLE_TASK_SCHEDULER_CONNECTION_STRING = "Endpoint=https://...;Taskhub=...;Authentication=DefaultAzure"
//   $env:DURABLETASK_STORAGE = "UseDevelopmentStorage=true"
//   dotnet run --framework net10.0 --no-launch-profile
//
// Config: ThresholdBytes=1KB, MaxPayloadBytes=15MB, Azurite blob storage.
//
// ---- Scenario Summary & Expected Output (verified 2026-04-01 against real DTS) ----
//
// [Scenario 1] Client oversized input (16MB > 15MB MaxPayloadBytes)
//   Validates: PayloadStorageException thrown immediately on client side
//   Expected: PASS: PayloadStorageException: Payload size 16384 KB exceeds the configured maximum...
//   Output:   PASS: PayloadStorageException: Payload size 16384 KB exceeds the configured maximum of 15360 KB.
//
// [Scenario 2] Activity oversized output (16MB > 15MB MaxPayloadBytes)
//   Validates: Orchestration fails gracefully (not infinite retry loop)
//   Expected: Status: Failed, PASS
//   Output:   Status: Failed / PASS / Error: Task 'ProduceOversized' (#0) failed...Payload size 16384 KB exceeds...
//
// [Scenario 3] Orchestration oversized output (16MB > 15MB MaxPayloadBytes)
//   Validates: Orchestration fails gracefully (not infinite retry loop)
//   Expected: Status: Failed, PASS
//   Output:   Status: Failed / PASS / Error: Payload size 16384 KB exceeds...
//
// [Scenario 4] 13MB activity input -> echo -> orchestration output
//   Validates: ValidateActionsSize bypass (13MB > 3.9MB chunk limit) + ScheduleTask.Input externalization
//              + ActivityResponse.Result externalization + CompleteOrchestration.Result externalization
//              + isChunkedMode: single oversized action sent as non-chunked (no ChunkIndex)
//   Expected: Status: Completed, Output length: 13631488, PASS
//   Output:   Status: Completed / Output length: 13631488 / PASS: 13MB activity output -> orch output externalized correctly
//
// [Scenario 5] 13MB orchestration input -> activity echo -> output
//   Validates: CreateInstanceRequest.Input externalization + replay with large input token in history
//   Expected: Status: Completed, PASS
//   Output:   Status: Completed / PASS
//
// [Scenario 6] 13MB sub-orchestration input -> child returns it
//   Validates: CreateSubOrchestration.Input externalization (>3.9MB action in orchestration completion)
//   Expected: Status: Completed, PASS
//   Output:   Status: Completed / PASS
//
// [Scenario 7] 13MB external event
//   Validates: RaiseEventRequest.Input externalization + WaitForExternalEvent resolution
//   Expected: Status: Completed, PASS
//   Output:   Status: Completed / PASS
//
// [Scenario 8] 13MB custom status
//   Validates: OrchestratorResponse.CustomStatus externalization + GetInstance resolution
//   Expected: Status: Completed, PASS
//   Output:   Status: Completed / PASS
//
// [Scenario 9] 3x 13MB activity inputs (real multi-chunk LP completion)
//   Validates: Multiple oversized ScheduleTask actions → real chunked completion with isChunkedMode=true
//              + all 3 activities execute and return correct data
//   Expected: Status: Completed, PASS: total=40894464
//   Output:   Status: Completed / PASS: total=40894464
//
// =================================================================================================

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Demonstrates Large Payload Externalization using Azure Blob Storage.
// This sample uses Azurite/emulator by default via UseDevelopmentStorage=true.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Connection string for Durable Task Scheduler
string schedulerConnectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

// 1) Register shared payload store ONCE
builder.Services.AddExternalizedPayloadStore(opts =>
{
    // Keep threshold small to force externalization for demo purposes
    opts.ThresholdBytes = 1024; // 1KB

    opts.MaxPayloadBytes = 15 * 1024 * 1024; // 15MB - allows 13MB scenario while still rejecting 16MB overflow
    opts.ConnectionString = builder.Configuration.GetValue<string>("DURABLETASK_STORAGE") ?? "UseDevelopmentStorage=true";
    opts.ContainerName = builder.Configuration.GetValue<string>("DURABLETASK_PAYLOAD_CONTAINER") ?? "payloads";
});

// 2) Configure Durable Task client
builder.Services.AddDurableTaskClient(b =>
{
    b.UseDurableTaskScheduler(schedulerConnectionString);
    b.Configure(o => o.EnableEntitySupport = true);

    // Use shared store (no duplication of options)
    b.UseExternalizedPayloads();
});

// 3) Configure Durable Task worker
builder.Services.AddDurableTaskWorker(b =>
{
    b.UseDurableTaskScheduler(schedulerConnectionString);

    b.AddTasks(tasks =>
    {
        // Orchestrator: call activity first, return its output (should equal original input)
        tasks.AddOrchestratorFunc<string, string>("LargeInputEcho", async (ctx, input) =>
        {
            string echoed = await ctx.CallActivityAsync<string>("Echo", input);
            return echoed;
        });

        // Activity: validate it receives raw input (not token) and return it
        tasks.AddActivityFunc<string, string>("Echo", (ctx, value) =>
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value.StartsWith("blob:v1:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Activity received a payload token instead of raw input.");
            }

            return value;
        });

        // Entity samples
        tasks.AddOrchestratorFunc<object?, int>(
            "LargeEntityOperationInput",
            (ctx, _) => ctx.Entities.CallEntityAsync<int>(
                new EntityInstanceId(nameof(EchoLengthEntity), "1"),
                operationName: "EchoLength",
                input: new string('E', 40 * 1024)));
        tasks.AddEntity<EchoLengthEntity>(nameof(EchoLengthEntity));

        tasks.AddOrchestratorFunc<object?, int>(
            "LargeEntityOperationOutput",
            async (ctx, _) => (await ctx.Entities.CallEntityAsync<string>(
                new EntityInstanceId(nameof(LargeResultEntity), "1"),
                operationName: "Produce",
                input: 40 * 1024)).Length);
        tasks.AddEntity<LargeResultEntity>(nameof(LargeResultEntity));

        tasks.AddOrchestratorFunc<object?, object?>(
            "LargeEntityState",
            async (ctx, _) =>
            {
                await ctx.Entities.CallEntityAsync(
                    new EntityInstanceId(nameof(StateEntity), "1"),
                    operationName: "Set",
                    input: new string('S', 40 * 1024));
                return null;
            });
        tasks.AddEntity<StateEntity>(nameof(StateEntity));

        // Overflow: activity output > MaxPayloadBytes
        tasks.AddOrchestratorFunc<object?, string>("ActivityProducesOversized", async (ctx, _) =>
        {
            return await ctx.CallActivityAsync<string>("ProduceOversized");
        });
        tasks.AddActivityFunc<string>("ProduceOversized", (ctx) => Task.FromResult(new string('O', 16 * 1024 * 1024)));

        // Overflow: orchestration output > MaxPayloadBytes
        tasks.AddOrchestratorFunc<object?, string>("OrchestrationProducesOversized", (ctx, _) =>
        {
            return Task.FromResult(new string('P', 16 * 1024 * 1024));
        });

        // 13MB scenarios: validates that ValidateActionsSize does not block externalization
        tasks.AddOrchestratorFunc<object?, string>("LargeActivityIO", async (ctx, _) =>
        {
            // Send 13MB as activity input, activity echoes it back, orchestration returns it
            string actResult = await ctx.CallActivityAsync<string>("Echo13MB", new string('M', 13 * 1024 * 1024));
            return actResult;
        });
        tasks.AddActivityFunc<string, string>("Echo13MB", (ctx, input) => input);

        // Scenario 5: 13MB orchestration input
        tasks.AddOrchestratorFunc<string, string>("LargeOrchInput", async (ctx, input) =>
        {
            return await ctx.CallActivityAsync<string>("Echo13MB", input);
        });

        // Scenario 6: 13MB sub-orchestration input
        tasks.AddOrchestratorFunc<object?, string>("LargeSubOrchParent", async (ctx, _) =>
        {
            return await ctx.CallSubOrchestratorAsync<string>("LargeSubOrchChild", new string('S', 13 * 1024 * 1024));
        });
        tasks.AddOrchestratorFunc<string, string>("LargeSubOrchChild", (ctx, input) => Task.FromResult(input));

        // Scenario 7: 13MB external event
        tasks.AddOrchestratorFunc<string>("LargeExternalEventOrch", async ctx =>
        {
            return await ctx.WaitForExternalEvent<string>("BigEvent");
        });

        // Scenario 8: 13MB custom status
        tasks.AddOrchestratorFunc<object?, string>("LargeCustomStatus", (ctx, _) =>
        {
            ctx.SetCustomStatus(new string('C', 13 * 1024 * 1024));
            return Task.FromResult("done");
        });

        // Scenario 9: 3x 13MB activity inputs (chunked orchestration completion)
        tasks.AddOrchestratorFunc<object?, int>("ThreelargeActivities", async (ctx, _) =>
        {
            var t1 = ctx.CallActivityAsync<string>("Echo13MB", new string('A', 13 * 1024 * 1024));
            var t2 = ctx.CallActivityAsync<string>("Echo13MB", new string('B', 13 * 1024 * 1024));
            var t3 = ctx.CallActivityAsync<string>("Echo13MB", new string('C', 13 * 1024 * 1024));
            string[] results = await Task.WhenAll(t1, t2, t3);
            return results.Sum(r => r.Length);
        });
    });

    // Use shared store (no duplication of options)
    b.UseExternalizedPayloads();

    b.Configure(o => o.EnableEntitySupport = true);
});

IHost host = builder.Build();
await host.StartAsync();

await using DurableTaskClient client = host.Services.GetRequiredService<DurableTaskClient>();

// Option A: Directly pass an oversized input to orchestration to trigger externalization
string largeInput = new string('B', 40 * 1024); // 40KB
string instanceId = await client.ScheduleNewOrchestrationInstanceAsync("LargeInputEcho", largeInput);
Console.WriteLine($"Started orchestration with direct large input. Instance: {instanceId}");

using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true,
    cts.Token);

Console.WriteLine($"RuntimeStatus: {result.RuntimeStatus}");
string deserializedInput = result.ReadInputAs<string>() ?? string.Empty;
string deserializedOutput = result.ReadOutputAs<string>() ?? string.Empty;

Console.WriteLine($"SerializedInput: {result.SerializedInput}");
Console.WriteLine($"SerializedOutput: {result.SerializedOutput}");
Console.WriteLine($"Deserialized input equals original: {deserializedInput == largeInput}");
Console.WriteLine($"Deserialized output equals original: {deserializedOutput == largeInput}");
Console.WriteLine($"Deserialized input length: {deserializedInput.Length}");

// Run entity samples
Console.WriteLine();
Console.WriteLine("Running LargeEntityOperationInput...");
string largeEntityInput = new string('E', 40 * 1024); // 40KB
string entityInputInstance = await client.ScheduleNewOrchestrationInstanceAsync("LargeEntityOperationInput");
OrchestrationMetadata entityInputResult = await client.WaitForInstanceCompletionAsync(entityInputInstance, getInputsAndOutputs: true, cts.Token);
int entityInputLength = entityInputResult.ReadOutputAs<int>();
Console.WriteLine($"Status: {entityInputResult.RuntimeStatus}, Input length: {entityInputLength}");
Console.WriteLine($"Deserialized input length equals original: {entityInputLength == largeEntityInput.Length}");

Console.WriteLine();
Console.WriteLine("Running LargeEntityOperationOutput...");
int largeEntityOutputLength = 40 * 1024; // 40KB
string entityOutputInstance = await client.ScheduleNewOrchestrationInstanceAsync("LargeEntityOperationOutput");
OrchestrationMetadata entityOutputResult = await client.WaitForInstanceCompletionAsync(entityOutputInstance, getInputsAndOutputs: true, cts.Token);
int entityOutputLength = entityOutputResult.ReadOutputAs<int>();
Console.WriteLine($"Status: {entityOutputResult.RuntimeStatus}, Output length: {entityOutputLength}");
Console.WriteLine($"Deserialized output length equals original: {entityOutputLength == largeEntityOutputLength}");

Console.WriteLine();
Console.WriteLine("Running LargeEntityState and querying state...");
string largeEntityState = new string('S', 40 * 1024); // 40KB
string entityStateInstance = await client.ScheduleNewOrchestrationInstanceAsync("LargeEntityState");
OrchestrationMetadata entityStateOrch = await client.WaitForInstanceCompletionAsync(entityStateInstance, getInputsAndOutputs: true, cts.Token);
Console.WriteLine($"Status: {entityStateOrch.RuntimeStatus}");
EntityMetadata<string>? state = await client.Entities.GetEntityAsync<string>(new EntityInstanceId(nameof(StateEntity), "1"), includeState: true);
int stateLength = state?.State?.Length ?? 0;
Console.WriteLine($"State length: {stateLength}");
Console.WriteLine($"Deserialized state equals original: {state?.State == largeEntityState}");

// ==================== Overflow Scenarios ====================
Console.WriteLine();
Console.WriteLine("=== Overflow Scenarios (MaxPayloadBytes=15MB) ===");

// Scenario 1: Client input > cap -> PayloadStorageException
Console.WriteLine("[Scenario 1] Client oversized input");
try
{
    string tooLarge = new string('Z', 16 * 1024 * 1024);
    await client.ScheduleNewOrchestrationInstanceAsync("LargeInputEcho", tooLarge);
    Console.WriteLine("ERROR: Expected PayloadStorageException!");
}
catch (PayloadStorageException ex)
{
    Console.WriteLine("PASS: " + ex.GetType().Name + ": " + ex.Message);
}
catch (Exception ex)
{
    Console.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message);
}

// Scenario 2: Activity output > cap -> orchestration fails
Console.WriteLine("[Scenario 2] Activity oversized output");
string actOvfId = await client.ScheduleNewOrchestrationInstanceAsync("ActivityProducesOversized");
OrchestrationMetadata actOvfResult = await client.WaitForInstanceCompletionAsync(actOvfId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + actOvfResult.RuntimeStatus);
Console.WriteLine(actOvfResult.RuntimeStatus == OrchestrationRuntimeStatus.Failed ? "PASS" : "FAIL: expected Failed");
if (actOvfResult.FailureDetails != null) Console.WriteLine("  Error: " + actOvfResult.FailureDetails.ErrorMessage);

// Scenario 3: Orchestration output > cap -> fails
Console.WriteLine("[Scenario 3] Orchestration oversized output");
string orchOvfId = await client.ScheduleNewOrchestrationInstanceAsync("OrchestrationProducesOversized");
OrchestrationMetadata orchOvfResult = await client.WaitForInstanceCompletionAsync(orchOvfId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + orchOvfResult.RuntimeStatus);
Console.WriteLine(orchOvfResult.RuntimeStatus == OrchestrationRuntimeStatus.Failed ? "PASS" : "FAIL: expected Failed");
if (orchOvfResult.FailureDetails != null) Console.WriteLine("  Error: " + orchOvfResult.FailureDetails.ErrorMessage);

// Scenario 4: 13MB activity output + orchestration output with LP enabled
// Tests that ValidateActionsSize bypass allows 13MB through the interceptor
Console.WriteLine();
Console.WriteLine("[Scenario 4] 13MB activity output -> orchestration output (round-trip)");
string largeIOId = await client.ScheduleNewOrchestrationInstanceAsync("LargeActivityIO");
OrchestrationMetadata largeIOResult = await client.WaitForInstanceCompletionAsync(largeIOId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + largeIOResult.RuntimeStatus);
if (largeIOResult.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
{
    string? orchOutput = largeIOResult.ReadOutputAs<string>();
    bool outputOk = orchOutput?.Length == 13 * 1024 * 1024;
    Console.WriteLine("  Output length: " + (orchOutput?.Length ?? 0));
    Console.WriteLine(outputOk ? "PASS: 13MB activity output -> orch output externalized correctly" : "FAIL: output length mismatch");
}
else
{
    Console.WriteLine("FAIL: Expected Completed, got " + largeIOResult.RuntimeStatus);
    if (largeIOResult.FailureDetails != null) Console.WriteLine("  Error: " + largeIOResult.FailureDetails.ErrorMessage);
}

// Scenario 5: 13MB orchestration input
Console.WriteLine();
Console.WriteLine("[Scenario 5] 13MB orchestration input -> activity echo -> orch output");
string orchInputId = await client.ScheduleNewOrchestrationInstanceAsync("LargeOrchInput", new string('I', 13 * 1024 * 1024));
OrchestrationMetadata orchInputResult = await client.WaitForInstanceCompletionAsync(orchInputId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + orchInputResult.RuntimeStatus);
Console.WriteLine(orchInputResult.RuntimeStatus == OrchestrationRuntimeStatus.Completed && orchInputResult.ReadOutputAs<string>()?.Length == 13 * 1024 * 1024
    ? "PASS" : "FAIL");
if (orchInputResult.FailureDetails != null) Console.WriteLine("  Error: " + orchInputResult.FailureDetails.ErrorMessage);

// Scenario 6: 13MB sub-orchestration input
Console.WriteLine();
Console.WriteLine("[Scenario 6] 13MB sub-orchestration input -> child returns it");
string subOrchId = await client.ScheduleNewOrchestrationInstanceAsync("LargeSubOrchParent");
OrchestrationMetadata subOrchResult = await client.WaitForInstanceCompletionAsync(subOrchId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + subOrchResult.RuntimeStatus);
Console.WriteLine(subOrchResult.RuntimeStatus == OrchestrationRuntimeStatus.Completed && subOrchResult.ReadOutputAs<string>()?.Length == 13 * 1024 * 1024
    ? "PASS" : "FAIL");
if (subOrchResult.FailureDetails != null) Console.WriteLine("  Error: " + subOrchResult.FailureDetails.ErrorMessage);

// Scenario 7: 13MB external event
Console.WriteLine();
Console.WriteLine("[Scenario 7] 13MB external event");
string extEvtId = await client.ScheduleNewOrchestrationInstanceAsync("LargeExternalEventOrch");
await client.WaitForInstanceStartAsync(extEvtId, cts.Token);
await client.RaiseEventAsync(extEvtId, "BigEvent", new string('E', 13 * 1024 * 1024), cts.Token);
OrchestrationMetadata extEvtResult = await client.WaitForInstanceCompletionAsync(extEvtId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + extEvtResult.RuntimeStatus);
Console.WriteLine(extEvtResult.RuntimeStatus == OrchestrationRuntimeStatus.Completed && extEvtResult.ReadOutputAs<string>()?.Length == 13 * 1024 * 1024
    ? "PASS" : "FAIL");
if (extEvtResult.FailureDetails != null) Console.WriteLine("  Error: " + extEvtResult.FailureDetails.ErrorMessage);

// Scenario 8: 13MB custom status
Console.WriteLine();
Console.WriteLine("[Scenario 8] 13MB custom status");
string csId = await client.ScheduleNewOrchestrationInstanceAsync("LargeCustomStatus");
OrchestrationMetadata csResult = await client.WaitForInstanceCompletionAsync(csId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + csResult.RuntimeStatus);
string? customStatus = csResult.ReadCustomStatusAs<string>();
Console.WriteLine(csResult.RuntimeStatus == OrchestrationRuntimeStatus.Completed && customStatus?.Length == 13 * 1024 * 1024
    ? "PASS" : "FAIL: custom status length=" + (customStatus?.Length ?? 0));

// Scenario 9: 3x 13MB activity inputs (chunked orchestration completion with LP)
Console.WriteLine();
Console.WriteLine("[Scenario 9] 3x 13MB activity inputs (chunked orch complete)");
string threeId = await client.ScheduleNewOrchestrationInstanceAsync("ThreelargeActivities");
OrchestrationMetadata threeResult = await client.WaitForInstanceCompletionAsync(threeId, getInputsAndOutputs: true, cts.Token);
Console.WriteLine("  Status: " + threeResult.RuntimeStatus);
int totalLen = threeResult.ReadOutputAs<int>();
Console.WriteLine(threeResult.RuntimeStatus == OrchestrationRuntimeStatus.Completed && totalLen == 3 * 13 * 1024 * 1024
    ? "PASS: total=" + totalLen : "FAIL: total=" + totalLen);
if (threeResult.FailureDetails != null) Console.WriteLine("  Error: " + threeResult.FailureDetails.ErrorMessage);

Console.WriteLine();
Console.WriteLine("=== ALL SCENARIOS DONE ===");

public class EchoLengthEntity : TaskEntity<int>
{
    public int EchoLength(string input)
    {
        return input.Length;
    }
}

public class LargeResultEntity : TaskEntity<object?>
{
    public string Produce(int length)
    {
        return new string('R', length);
    }
}

public class StateEntity : TaskEntity<string?>
{
    protected override string? InitializeState(TaskEntityOperation entityOperation)
    {
        // Avoid Activator.CreateInstance<string>() which throws; start as null (no state)
        return null;
    }

    public void Set(string value)
    {
        this.State = value;
    }
}