// ----------------------------------------------------------------------------------
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using DurableTask.Core;

namespace DurableTask;

public class TaskActivityWrapper<TOutput> : TaskActivity
{
    readonly TaskName name;
    readonly Func<ITaskActivityContext, Task<TOutput?>> wrappedImplementation;

    readonly IDataConverter dataConverter;

    public TaskActivityWrapper(
        IWorkerContext workerContext,
        TaskName name,
        Func<ITaskActivityContext, Task<TOutput?>> implementation)
    {
        this.dataConverter = workerContext.DataConverter ?? JsonDataConverter.Default;
        this.name = name;
        this.wrappedImplementation = implementation;
    }

    public override async Task<string?> RunAsync(TaskContext coreContext, string rawInput)
    {
        string? sanitizedInput = StripArrayCharacters(rawInput);
        TaskActivityContextWrapper contextWrapper = new(coreContext, this.name, sanitizedInput, this.dataConverter);
        object? output = await this.wrappedImplementation.Invoke(contextWrapper);

        // Return the output (if any) as a serialized string.
        string? serializedOutput = output != null ? this.dataConverter.Serialize(output) : null;
        return serializedOutput;
    }

    static string? StripArrayCharacters(string? input)
    {
        if (input != null && input.StartsWith('[') && input.EndsWith(']'))
        {
            // Strip the outer bracket characters
            return input[1..^1];
        }

        return input;
    }

    // Not used/called
    public override string Run(TaskContext context, string input) => throw new NotImplementedException();

    sealed class TaskActivityContextWrapper : ITaskActivityContext
    {
        readonly TaskContext innerContext;
        readonly TaskName name;
        readonly string? rawInput;
        readonly IDataConverter dataConverter;

        public TaskActivityContextWrapper(
            TaskContext taskContext,
            TaskName name,
            string? rawInput,
            IDataConverter dataConverter)
        {
            this.innerContext = taskContext;
            this.name = name;
            this.rawInput = rawInput;
            this.dataConverter = dataConverter;
        }

        public TaskName Name => this.name;

        public string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;

        public T GetInput<T>()
        {
            if (this.rawInput == null)
            {
                return default!;
            }

            return this.dataConverter.Deserialize<T>(this.rawInput)!;
        }
    }
}
