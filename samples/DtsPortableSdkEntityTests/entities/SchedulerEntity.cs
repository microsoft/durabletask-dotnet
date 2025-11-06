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

class SchedulerEntity : ITaskEntity
{
    private readonly ILogger logger;

    public SchedulerEntity(ILogger<SchedulerEntity> logger)
    {
        this.logger = logger;
    }

    public ValueTask<object?> RunAsync(TaskEntityOperation operation)
    {
        this.logger.LogInformation("{entityId} received {operationName} signal", operation.Context.Id, operation.Name);

        List<string> state = (List<string>?)operation.State.GetState(typeof(List<string>)) ?? new List<string>();

        if (state.Contains(operation.Name))
        {
            this.logger.LogError($"duplicate: {operation.Name}");
        }
        else
        {
            state.Add(operation.Name);
        }

        return default;
    }

    public static void Register(DurableTaskRegistry r)
    {
        r.AddEntity(
            nameof(SchedulerEntity),
            (IServiceProvider serviceProvider) =>
                (ITaskEntity)ActivatorUtilities.GetServiceOrCreateInstance<SchedulerEntity>(serviceProvider)!);
    }
}
