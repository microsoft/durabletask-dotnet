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

using System;
using System.Threading.Tasks;

namespace DurableTask;

// TODO: Overloads for class-based factory types
// TODO: Documentation
public interface ITaskOrchestrationBuilder
{
    public ITaskOrchestrationBuilder AddTaskOrchestrator(
        TaskName name,
        Func<TaskOrchestrationContext, Task> implementation);

    public ITaskOrchestrationBuilder AddTaskOrchestrator<T>(
        TaskName name,
        Func<TaskOrchestrationContext, Task<T>> implementation);

    public ITaskOrchestrationBuilder AddTaskActivity(
        TaskName name,
        Func<TaskActivityContext, object?> implementation);

    public ITaskOrchestrationBuilder AddTaskActivity(
        TaskName name,
        Func<TaskActivityContext, Task> implementation);

    public ITaskOrchestrationBuilder AddTaskActivity<T>(
        TaskName name,
        Func<TaskActivityContext, Task<T>> implementation);
}
