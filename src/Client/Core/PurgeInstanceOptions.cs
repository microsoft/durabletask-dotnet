// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Options to purge an orchestration.
/// </summary>
/// <param name="Recursive">The optional boolean value indicating whether to purge sub-orchestrations as well.</param>
public record PurgeInstanceOptions(bool Recursive = false);
