// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace ExceptionPropertiesSample;

/// <summary>
/// Orchestration that demonstrates custom exception properties in failure details.
/// </summary>
[DurableTask("ValidationOrchestration")]
public class ValidationOrchestration : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        // Call an activity that may throw a custom exception with properties
        try
        {
            string result = await context.CallActivityAsync<string>("ValidateInput", input);
            return result;
        }
        catch (TaskFailedException ex)
        {
            // The failure details will include custom properties from IExceptionPropertiesProvider
            // These properties are automatically extracted and included in the TaskFailureDetails
            throw;
        }
    }
}

/// <summary>
/// Activity that validates input and throws a custom exception with properties on failure.
/// </summary>
[DurableTask("ValidateInput")]
public class ValidateInputActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
    {
        // Simulate validation logic
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new BusinessValidationException(
                message: "Input validation failed: input cannot be empty",
                errorCode: "VALIDATION_001",
                statusCode: 400,
                metadata: new Dictionary<string, object?>
                {
                    ["Field"] = "input",
                    ["ValidationRule"] = "Required",
                    ["Timestamp"] = DateTime.UtcNow,
                });
        }

        if (input.Length < 3)
        {
            throw new BusinessValidationException(
                message: $"Input validation failed: input must be at least 3 characters (received {input.Length})",
                errorCode: "VALIDATION_002",
                statusCode: 400,
                metadata: new Dictionary<string, object?>
                {
                    ["Field"] = "input",
                    ["ValidationRule"] = "MinLength",
                    ["MinLength"] = 3,
                    ["ActualLength"] = input.Length,
                });
        }

        // Validation passed
        return Task.FromResult($"Validation successful for input: {input}");
    }
}

