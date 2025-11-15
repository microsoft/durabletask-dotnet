// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Failure of a specific instance export.
/// </summary>
/// <param name="InstanceId">The instance ID that failed to export.</param>
/// <param name="Reason">The reason for the failure.</param>
/// <param name="AttemptCount">The number of attempts made.</param>
/// <param name="LastAttempt">The timestamp of the last attempt.</param>
public sealed record ExportFailure(string InstanceId, string Reason, int AttemptCount, DateTimeOffset LastAttempt);
