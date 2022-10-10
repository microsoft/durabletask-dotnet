// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Represents a parent orchestration details.
/// </summary>
/// <param name="Name">The name of the parent orchestration.</param>
/// <param name="InstanceId">The instance ID of the parent orchestration.</param>
public record ParentOrchestrationInstance(TaskName Name, string InstanceId);
