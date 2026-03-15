// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This sample demonstrates how the source generator places extension methods into the same
// namespace as the orchestrator/activity classes, keeping IDE suggestions clean and scoped.
// Tasks in different namespaces get their own GeneratedDurableTaskExtensions class.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;

// The generated AddAllGeneratedTasks() method is always in Microsoft.DurableTask namespace.
// Extension methods like ScheduleNewApprovalOrchestratorInstanceAsync() are in the
// NamespaceGenerationSample.Approvals namespace, and CallRegistrationActivityAsync() is in
// NamespaceGenerationSample.Registrations namespace.
using NamespaceGenerationSample.Approvals;

// Start the in-process test host (no external services needed)
// The generated AddAllGeneratedTasks() registers all orchestrators and activities
await using DurableTaskTestHost testHost = await DurableTaskTestHost.StartAsync(
    registry => registry.AddAllGeneratedTasks());

DurableTaskClient client = testHost.Client;

// Use the generated typed extension method (in the Approvals namespace)
string instanceId = await client.ScheduleNewApprovalOrchestratorInstanceAsync("request-123");
Console.WriteLine($"Started approval orchestration: {instanceId}");

// Wait for completion
OrchestrationMetadata? result = await client.WaitForInstanceCompletionAsync(
    instanceId, getInputsAndOutputs: true);
Console.WriteLine($"Orchestration completed with status: {result?.RuntimeStatus}");
Console.WriteLine($"Output: {result?.ReadOutputAs<string>()}");
