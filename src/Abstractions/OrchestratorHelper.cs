// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// Orchestration/Activity input helpers.
/// </summary>
static class InputHelper
{
    /// <summary>
    /// Due to nullable reference types being static analysis only, we need to do our best efforts for validating the
    /// input type, but also give control of nullability to the implementation. It is not ideal, but we do not want to
    /// force 'TInput?' on the RunAsync implementation.
    /// </summary>
    /// <typeparam name="TInput">The input type of the orchestration or activity.</typeparam>
    /// <param name="input">The input object.</param>
    /// <param name="typedInput">The input converted to the desired type.</param>
    public static void ValidateInput<TInput>(object? input, out TInput typedInput)
    {
        if (input is TInput typed)
        {
            // Quick pattern check.
            typedInput = typed;
            return;
        }
        else if (input is not null && typeof(TInput) != input.GetType())
        {
            throw new ArgumentException($"Input type '{input?.GetType()}' does not match expected type '{typeof(TInput)}'.");
        }

        // Input is null and did not match a nullable value type. We do not have enough information to tell if it is
        // valid or not. We will have to defer this decision to the implementation. Additionally, we will coerce a null
        // input to a default value type here. This is to keep the two RunAsync(context, default) overloads to have
        // identical behavior.
        typedInput = default!;
        return;
    }
}
