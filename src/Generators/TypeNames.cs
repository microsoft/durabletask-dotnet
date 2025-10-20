// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Generators;

/// <summary>
/// Static attribute names.
/// </summary>
static class TypeNames
{
    /// <summary>
    /// Gets the <c>DurableTaskAttribute</c> attribute full metadata name.
    /// </summary>
    public const string DurableTaskAttribute = "Microsoft.DurableTask.DurableTaskAttribute";

    /// <summary>
    /// Gets the <c>ITaskOrchestrator</c> interface full metadata name.
    /// </summary>
    public const string TaskOrchestratorInterface = "Microsoft.DurableTask.ITaskOrchestrator";

    /// <summary>
    /// Gets the <c>ITaskActivity</c> interface full metadata name.
    /// </summary>
    public const string TaskActivityInterface = "Microsoft.DurableTask.ITaskActivity";

    /// <summary>
    /// Gets the <c>ITaskEntity</c> interface full metadata name.
    /// </summary>
    public const string TaskEntityInterface = "Microsoft.DurableTask.Entities.ITaskEntity";

    /// <summary>
    /// Gets the <c>DurableTaskRegistry</c> class full metadata name.
    /// </summary>
    public const string RegistryClass = "Microsoft.DurableTask.DurableTaskRegistry";

    /// <summary>
    /// Gets the <c>TaskOptions</c> class full metadata name.
    /// </summary>
    public const string TaskOptionsClass = "Microsoft.DurableTask.TaskOptions";

    /// <summary>
    /// Gets the <c>TaskOrchestrationContext</c> class full metadata name.
    /// </summary>
    public const string OrchestratorContextClass = "Microsoft.DurableTask.TaskOrchestrationContext";
}