// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// Interface for validating task inputs.
/// </summary>
public interface IInputValidator
{
    /// <summary>
    /// Validates the input for the specified task.
    /// </summary>
    /// <param name="taskName">The name of the task being validated.</param>
    /// <param name="input">The input to validate.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidateAsync(TaskName taskName, object? input);
}
