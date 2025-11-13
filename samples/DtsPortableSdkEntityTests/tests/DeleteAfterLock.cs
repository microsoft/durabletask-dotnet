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

class DeleteAfterLock : Test
{
    public override async Task RunAsync(TestContext context)
    {
        // ----- first, delete all already-existing entities in storage to ensure queries have predictable results
        context.Logger.LogInformation("deleting existing entities");

        // run a purge to force a flush, otherwise our query may miss some results
        await context.Client.PurgeAllInstancesAsync(new PurgeInstancesFilter() { CreatedFrom = DateTime.MinValue }, context.CancellationToken);

        List<Task> tasks = [];
        await foreach (var entity in context.Client.Entities.GetAllEntitiesAsync(new EntityQuery()))
        {
            tasks.Add(context.Client.PurgeInstanceAsync(entity.Id.ToString(), context.CancellationToken));
        }
        await Task.WhenAll(tasks);

        // check that a blank entity query returns no elements now
        var e = context.Client.Entities.GetAllEntitiesAsync(new EntityQuery()).GetAsyncEnumerator();
        Assert.False(await e.MoveNextAsync());

        // -------------- then, lock an entity without ever creating state ... so it should disappear afterwards

        var entityId = new EntityInstanceId(nameof(Counter), $"delete-after-lock-{Guid.NewGuid()}");
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(nameof(LockEntityWithoutCallOrchestration), entityId);
        var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());

        // check that entity state is correctly reported as non-existing
        EntityMetadata<int>? entityMetadata = await context.Client.Entities.GetEntityAsync<int>(entityId, includeState: true);
        Assert.Null(entityMetadata);

        // check that the entity shows up as a transient entity if it has not been automatically deleted 
        var list = context.Client.Entities!.GetAllEntitiesAsync(new EntityQuery
        {
            InstanceIdStartsWith = entityId.ToString(),
            IncludeTransient = true,
        }).ToBlockingEnumerable().ToList();

        if (!context.BackendSupportsImplicitEntityDeletion)
        {
            Assert.Single(list);
            var cleaningResponse = await context.Client.Entities.CleanEntityStorageAsync();
            Assert.Equal(1, cleaningResponse.EmptyEntitiesRemoved);
        }
        else
        {
            Assert.Empty(list);
        }
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<LockEntityWithoutCallOrchestration>();
    }

    public class LockEntityWithoutCallOrchestration : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
        {
            await using (var lockContext = await context.Entities.LockEntitiesAsync(entityId))
            {
                // don't do anything with the lock, we only lock the entity but don't create state
            };

            return "ok";
        }
    }
}
