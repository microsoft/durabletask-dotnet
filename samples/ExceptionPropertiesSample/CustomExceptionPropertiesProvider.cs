// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker;

namespace ExceptionPropertiesSample;

/// <summary>
/// Custom exception properties provider that extracts additional properties from exceptions
/// to include in TaskFailureDetails for better diagnostics and error handling.
/// </summary>
public class CustomExceptionPropertiesProvider : IExceptionPropertiesProvider
{
    /// <summary>
    /// Extracts custom properties from exceptions to enrich failure details.
    /// </summary>
    /// <param name="exception">The exception to extract properties from.</param>
    /// <returns>
    /// A dictionary of custom properties to include in the FailureDetails,
    /// or null if no properties should be added for this exception type.
    /// </returns>
    public IDictionary<string, object?>? GetExceptionProperties(Exception exception)
    {
        return exception switch
        {
            BusinessValidationException businessEx => new Dictionary<string, object?>
            {
                ["ErrorCode"] = businessEx.ErrorCode,
                ["StatusCode"] = businessEx.StatusCode,
                ["Metadata"] = businessEx.Metadata,
            },
            ArgumentOutOfRangeException argEx => new Dictionary<string, object?>
            {
                ["ParameterName"] = argEx.ParamName ?? string.Empty,
                ["ActualValue"] = argEx.ActualValue?.ToString() ?? string.Empty,
            },
            ArgumentNullException argNullEx => new Dictionary<string, object?>
            {
                ["ParameterName"] = argNullEx.ParamName ?? string.Empty,
            },
            _ => null // No custom properties for other exception types
        };
    }
}

