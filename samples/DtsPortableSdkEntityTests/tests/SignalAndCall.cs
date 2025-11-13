// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Xunit;

namespace DtsPortableSdkEntityTests;

class SignalAndCall : Test
{
    readonly Type entityType;

    public SignalAndCall(Type entityType)
    {
        this.entityType = entityType;
    }

    public override string Name => $"{base.Name}.{entityType.Name}";

    public override async Task RunAsync(TestContext context)
    {
        var entityId = new EntityInstanceId(this.entityType.Name, Guid.NewGuid().ToString().Substring(0, 8));

        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(nameof(SignalAndCallOrchestration), entityId, context.CancellationToken);
        OrchestrationMetadata metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs:true, context.CancellationToken);
        
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<SignalAndCallOrchestration>();
    }

    public class SignalAndCallOrchestration : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entity)
        {
            // signal and call (both of these will be delivered close together, typically in the same batch, and always in order)
            await context.Entities.SignalEntityAsync(entity, "set", "333");

            string? result = await context.Entities.CallEntityAsync<string?>(entity, "get");

            if (result != "333")
            {
                return $"fail: wrong entity state: expected 333, got {result}";
            }

            // make another call to see if the state survives replay
            result = await context.Entities.CallEntityAsync<string?>(entity, "get");

            if (result != "333")
            {
                return $"fail: wrong entity state: expected 333 still, but got {result}";
            }

            return "ok";
        }
    }
}
