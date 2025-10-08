// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Provides custom exception property inclusion rules for enriching FailureDetails.
/// </summary>
public interface IExceptionPropertiesProvider
{
        /// <summary>
        /// Extracts custom properties from an exception.
        /// </summary>
        /// <param name="exception">The exception to extract properties from.</param>
        /// <returns>A dictionary of custom properties to include in the FailureDetails, or null if no properties should be added.</returns>
        IDictionary<string, object?>? GetExceptionProperties(Exception exception);
}
