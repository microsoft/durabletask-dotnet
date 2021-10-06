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

using System.Threading.Tasks;

namespace DurableTask;

// TODO: Move to separate file
public interface ITaskOrchestrator
{
    Task<object?> RunAsync(TaskOrchestrationContext context);
}

public abstract class TaskOrchestratorBase<TInput, TOutput> : ITaskOrchestrator
{
    protected internal TaskOrchestrationContext Context { get; internal set; } = default!;

    protected abstract Task<TOutput> OnRunAsync(TInput? input);

    async Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context)
    {
        this.Context = context;
        TInput? input = context.GetInput<TInput>();
        object? output = await this.OnRunAsync(input);
        return output;
    }
}
