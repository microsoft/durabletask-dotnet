// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace EventsSample;

/// <summary>
/// Example event type annotated with DurableEventAttribute.
/// This generates a strongly-typed WaitForApprovalEventAsync method.
/// </summary>
[DurableEvent(nameof(ApprovalEvent))]
public sealed record ApprovalEvent(bool Approved, string? Approver);

/// <summary>
/// Another example event type with custom name.
/// This generates a WaitForDataReceivedAsync method that waits for "DataReceived" event.
/// </summary>
[DurableEvent("DataReceived")]
public sealed record DataReceivedEvent(int Id, string Data);
