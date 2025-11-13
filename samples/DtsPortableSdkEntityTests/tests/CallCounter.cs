// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Xunit;

namespace DtsPortableSdkEntityTests;

class CallCounter : Test
{
    public override async Task RunAsync(TestContext context)
    {
        var entityId = new EntityInstanceId(nameof(Counter), Guid.NewGuid().ToString());
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(nameof(CallCounterOrchestration), entityId);
        var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("OK", metadata.ReadOutputAs<string>());

        // entity ids cannot be used for orchestration instance queries
        await Assert.ThrowsAsync<ArgumentException>(() => context.Client.GetInstanceAsync(entityId.ToString()));
       
        // and are not returned by them 
        List<OrchestrationMetadata> results = context.Client.GetAllInstancesAsync().ToBlockingEnumerable().ToList();
        Assert.DoesNotContain(results, metadata => metadata.InstanceId.StartsWith("@"));

        // check that entity state is correct
        EntityMetadata<int>? entityMetadata = await context.Client.Entities.GetEntityAsync<int>(entityId, includeState:true);
        Assert.NotNull(entityMetadata);
        Assert.Equal(33, entityMetadata!.State);
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<CallCounterOrchestration>();
    }

    public class CallCounterOrchestration : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
        {
          
            await context.Entities.CallEntityAsync(entityId, "set", 33);
            int result = await context.Entities.CallEntityAsync<int>(entityId, "get");

            if (result == 33)
            {
                return "OK";
            }
            else
            {
                return $"wrong result: {result} instead of 33";
            }
        }
    }
}
