// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Constants for export job entity operation names.
/// </summary>
static class ExportJobOperations
{
    /// <summary>
    /// Operation name for getting entity state.
    /// </summary>
    public const string Get = "get";

    /// <summary>
    /// Operation name for deleting the entity.
    /// </summary>
    public const string Delete = "delete";
}

