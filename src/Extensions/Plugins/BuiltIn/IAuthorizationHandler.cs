// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// Handler for evaluating authorization rules on task executions.
/// </summary>
public interface IAuthorizationHandler
{
    /// <summary>
    /// Evaluates whether the given task execution should be authorized.
    /// </summary>
    /// <param name="context">The authorization context.</param>
    /// <returns><c>true</c> if execution is authorized; <c>false</c> otherwise.</returns>
    Task<bool> AuthorizeAsync(AuthorizationContext context);
}
