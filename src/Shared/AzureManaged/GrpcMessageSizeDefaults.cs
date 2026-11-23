// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Default gRPC message size limits for Durable Task Scheduler channels.
/// </summary>
internal static class GrpcMessageSizeDefaults
{
    /// <summary>
    /// The maximum outgoing message size in bytes, matching the scheduler's default receive limit.
    /// </summary>
    public const int MaxSendMessageSize = 4 * 1024 * 1024;
}
