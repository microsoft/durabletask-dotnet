//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

namespace DurableTask;

/// <summary>
/// Abstract base class for task activity context.
/// </summary>
/// <remarks>
/// A new instance of <see cref="TaskActivityContext"/> is passed as a parameter to each
/// task activity execution. It includes basic information such as the name of the activity, the
/// ID of the invoking orchestration instance, and a method for reading the activity input.
/// </remarks>
public abstract class TaskActivityContext
{
    /// <summary>
    /// Gets the name of the task activity.
    /// </summary>
    public abstract TaskName Name { get; }

    /// <summary>
    /// Gets the unique ID of the current orchestration instance.
    /// </summary>
    public abstract string InstanceId { get; }

    /// <summary>
    /// Gets the task activity's input.
    /// </summary>
    /// <typeparam name="T">The type of the activity input. This is used for deserialization.</typeparam>
    /// <returns>Returns the input deserialized into an object of type <c>T</c>.</returns>
    public abstract T? GetInput<T>();
}
