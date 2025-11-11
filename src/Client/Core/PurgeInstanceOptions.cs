// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Options to purge an orchestration.
/// Note that recursive purging of suborchestrations is currently not supported.
/// </summary>
/// <param name="Recursive">The optional boolean value indicating whether to purge sub-orchestrations as well.
/// Currently this parameter is not supported.</param>
public record PurgeInstanceOptions(bool Recursive = false);
