// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DurableTask.Core.Entities;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DtsPortableSdkEntityTests;

class CallMultipleFaultyEntities : Test
{
    public override async Task RunAsync(TestContext context)
    {
        var entityId = new EntityInstanceId(nameof(FaultyEntity), Guid.NewGuid().ToString());
        string orchestrationName = nameof(CallFaultyEntityBatchesOrchestration);
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, entityId);
        var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<CallFaultyEntityBatchesOrchestration>();
    }
}

class CallFaultyEntityBatchesOrchestration : TaskOrchestrator<EntityInstanceId, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
    {

        // we use this utility function to try to enforce that a bunch of signals is delivered as a single batch.
        // This is required for some of the tests here to work, since the batching affects the entity state management.
        // The "enforcement" mechanism we use is not 100% failsafe (it still makes timing assumptions about the provider)
        // but it should be more reliable than the original version of this test which failed quite frequently, as it was
        // simply assuming that signals that are sent at the same time are always processed as a batch.
        async Task ProcessSignalBatch(IEnumerable<(string, int?)> signals)
        {
            // first issue a signal that, when delivered, keeps the entity busy for a split second
            await context.Entities.SignalEntityAsync(entityId, "Delay", 0.5);

            // we now need to yield briefly so that the delay signal is sent before the others
            await context.CreateTimer(context.CurrentUtcDateTime + TimeSpan.FromMilliseconds(1), CancellationToken.None);

            // now send the signals one by one. These should all arrive and get queued (inside the storage provider)
            // while the entity is executing the delay operation. Therefore, after the delay operation finishes,
            // all of the signals are processed in a single batch.
            foreach ((string operation, int? arg) in signals)
            {
                await context.Entities.SignalEntityAsync(entityId, operation, arg);
            }
        }

        try
        {
            await ProcessSignalBatch(new (string, int?)[]
            {
                new("Set", 42), // state that survives
                new("SetThenThrow", 333),
                new("DeleteThenThrow", null),
            });

            Assert.Equal(42, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ProcessSignalBatch(new (string, int?)[]
            {
                new("Get", null),
                new("Set", 42),
                new("Delete", null),
                new("Set", 43), // state that survives
                new("DeleteThenThrow", null),
            });

            Assert.Equal(43, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ProcessSignalBatch(new (string, int?)[]
            {
                new("Set", 55), // state that survives
                new("SetToUnserializable", null),
            });


            Assert.Equal(55, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ProcessSignalBatch(new (string, int?)[]
            {
                new("Set", 1),
                new("Delete", null),
                new("Set", 2),
                new("Delete", null), // state that survives
                new("SetThenThrow", 333),
            });

            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ProcessSignalBatch(new (string, int?)[]
            {
                new("Set", 1),
                new("Delete", null),
                new("Set", 2),
                new("Delete", null), // state that survives
                new("SetThenThrow", 333),
            });

            // must have rolled back to non-existing state
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ProcessSignalBatch(new (string, int?)[]
            {
                new("Set", 1),
                new("SetThenThrow", 333),
                new("Set", 2),
                new("DeleteThenThrow", null),
                new("Delete", null),
                new("Set", 3), // state that survives
            });

            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            return "ok";
        }
        catch (Exception e)
        {
            return e.ToString();
        }
    }
}