// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Constants for export job entity operation names.
/// </summary>
/// <remarks>
/// Operation names are case-insensitive when matching to entity methods.
/// These constants match the method names on <see cref="ExportJob"/> for consistency.
/// </remarks>
static class ExportJobOperations
{
    /// <summary>
    /// Operation name for deleting the entity.
    /// </summary>
    public const string Delete = "Delete";
}

