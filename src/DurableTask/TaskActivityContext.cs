// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask;

/// <summary>
/// Defines properties and methods for task activity context objects.
/// </summary>
/// <remarks>
/// A new instance of <see cref="TaskActivityContext"/> is passed as a parameter to each
/// task activity execution. It includes basic information such as the name of the activity, the
/// ID of the invoking orchestration instance, and a method for reading the activity input.
/// </remarks>
public abstract class TaskActivityContext
{
    // IMPORTANT: This abstract class is implemented in the output of source generators, so any changes
    //            to the interface may also need to be reflected in the source generator output.

    /// <summary>
    /// Gets the name of the task activity.
    /// </summary>
    public abstract TaskName Name { get; }

    /// <summary>
    /// Gets the unique ID of the current orchestration instance.
    /// </summary>
    public abstract string InstanceId { get; }
}
