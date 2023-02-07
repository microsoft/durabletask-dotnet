// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Contract for providing input to an orchestration or activity.
/// </summary>
interface IProvidesInput
{
    /// <summary>
    /// Gets the input for the orchestration or activity.
    /// </summary>
    /// <returns>The input value.</returns>
    /// <remarks>
    /// This is a method and not a property to ensure it is not included in serialization.
    /// </remarks>
    object? GetInput();
}
