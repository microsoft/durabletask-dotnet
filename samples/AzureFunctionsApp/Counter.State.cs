// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;

namespace AzureFunctionsApp.State;

/// <summary>
/// Example on how to dispatch to a POCO as the entity implementation.
/// </summary>
public static class Counter
{
    [Function("Counter1")]
    public static Task RunAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        return dispatcher.DispatchAsync<State>();
    }

    class State
    {
        public int Value { get; set; }

        public int Add(int input) => this.Value += input;

        public int Get() => this.Value;
    }
}
