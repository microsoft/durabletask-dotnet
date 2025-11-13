// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace DtsPortableSdkEntityTests;

/// <summary>
/// A stateless entity that forwards signals
/// </summary>
class Relay : ITaskEntity
{
    public record Input(EntityInstanceId entityInstanceId, string operationName, object? input, DateTimeOffset? scheduledTime);

    public ValueTask<object?> RunAsync(TaskEntityOperation operation)
    {
        T GetInput<T>() => (T)operation.GetInput(typeof(T))!;

        Input input = GetInput<Input>();

        operation.Context.SignalEntity(
            input.entityInstanceId, 
            input.operationName, 
            input.input,
            new SignalEntityOptions() { SignalTime = input.scheduledTime });

        return default;
    }

    public static void Register(DurableTaskRegistry r)
    {
        r.AddEntity(nameof(Relay), _ => new Relay());
    }
}
