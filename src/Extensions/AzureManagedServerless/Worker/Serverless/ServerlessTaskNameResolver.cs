// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureManaged.Serverless;

/// <summary>
/// Resolves Durable Task names for serverless activity declaration APIs.
/// </summary>
static class ServerlessTaskNameResolver
{
    /// <summary>
    /// Gets the Durable Task name for the specified task type.
    /// </summary>
    /// <param name="type">The task type.</param>
    /// <returns>The resolved task name.</returns>
    public static string GetTaskName(Type type)
    {
        Check.NotNull(type);
        return Attribute.GetCustomAttribute(type, typeof(DurableTaskAttribute)) is DurableTaskAttribute { Name.Name: not null and not "" } attr
            ? attr.Name.Name
            : type.Name;
    }
}
