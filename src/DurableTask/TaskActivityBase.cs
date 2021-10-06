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

// TODO: Move to separate file
public interface ITaskActivity
{
    Task<object?> RunAsync(TaskActivityContext context);
}

public abstract class TaskActivityBase<TInput, TOutput> : ITaskActivity
{
    protected internal TaskActivityContext Context { get; internal set; } = NullActivityContext.Instance;

    protected virtual Task<TOutput?> OnRunAsync(TInput? input) => Task.FromResult(this.OnRun(input));

    protected virtual TOutput? OnRun(TInput? input) => throw this.DefaultNotImplementedException();

    async Task<object?> ITaskActivity.RunAsync(TaskActivityContext context)
    {
        this.Context = context;
        TInput? input = context.GetInput<TInput>();
        object? output = await this.OnRunAsync(input);
        return output;
    }

    Exception DefaultNotImplementedException()
    {
        return new NotImplementedException($"{this.GetType().Name} needs to override {nameof(this.OnRun)} or {nameof(this.OnRunAsync)} with an implementation.");
    }

    class NullActivityContext : TaskActivityContext
    {
        public static NullActivityContext Instance { get; } = new NullActivityContext();

        public override TaskName Name => string.Empty;

        public override string InstanceId => string.Empty;

        public override T GetInput<T>() => throw new NotImplementedException();
    }
}

// TODO: Move to separate file
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DurableTaskAttribute : Attribute
{
    public DurableTaskAttribute(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// Gets the name of the durable task.
    /// </summary>
    public TaskName Name {  get; }
}
