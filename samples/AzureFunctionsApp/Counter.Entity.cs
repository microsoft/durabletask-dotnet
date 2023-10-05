// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Entities;

namespace AzureFunctionsApp.Entity;

/// <summary>
/// Example on how to dispatch to an entity which directly implements TaskEntity<TState>.
/// </summary>
public class Counter : TaskEntity<int>
{
    public int Add(int input) => this.State += input;

    public int Get() => this.State;

    [Function("Counter2")]
    public Task RunAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        return dispatcher.DispatchAsync(this);
    }
}
