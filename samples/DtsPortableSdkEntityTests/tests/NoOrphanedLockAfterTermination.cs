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

class NoOrphanedLockAfterTermination : Test
{

    public override async Task RunAsync(TestContext context)
    {
        DateTime startTime = DateTime.UtcNow;

        // construct unique names for this test
        string prefix = Guid.NewGuid().ToString("N").Substring(0, 6);
        var orphanedEntityId = new EntityInstanceId(nameof(Counter), $"{prefix}-orphaned");
        var orchestrationA = $"{prefix}-A";
        var orchestrationB = $"{prefix}-B";

        // start an orchestration A that acquires the lock and then waits forever
        await context.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(InfiniteLocker),
            orphanedEntityId,
            new StartOrchestrationOptions() { InstanceId = orchestrationA },
            context.CancellationToken);
        await context.Client.WaitForInstanceStartAsync(orchestrationA, context.CancellationToken);

        // start an orchestration B that queues behind A for the lock
        await context.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(LockingIncrementor),
            orphanedEntityId,
            new StartOrchestrationOptions() { InstanceId = orchestrationB },
            context.CancellationToken);
        await context.Client.WaitForInstanceStartAsync(orchestrationB, context.CancellationToken);

        // try to get the entity using a point query. The result is null because the entitiy is transient.
        EntityMetadata? entityMetadata = await context.Client.Entities.GetEntityAsync(orphanedEntityId, context.CancellationToken);
        Assert.Null(entityMetadata);

        // try to get the entity state using a query that does not include transient states. SHould not return anything.
        List<EntityMetadata> results = context.Client.Entities.GetAllEntitiesAsync(
            new Microsoft.DurableTask.Client.Entities.EntityQuery
            {
                InstanceIdStartsWith = orphanedEntityId.ToString(),
                IncludeTransient = false,
                IncludeState = true,
            }).ToBlockingEnumerable().ToList();
        Assert.Empty(results);

        // try to get the entity state using a query that includes transient states. This should return the entity.
        results = context.Client.Entities.GetAllEntitiesAsync(
            new Microsoft.DurableTask.Client.Entities.EntityQuery {
                InstanceIdStartsWith = orphanedEntityId.ToString(),
                IncludeTransient = true,
                IncludeState = true,
            }).ToBlockingEnumerable().ToList();
        Assert.Single(results);
        Assert.Equal(orphanedEntityId, results[0].Id);
        Assert.False(results[0].IncludesState);
        Assert.True(results[0].LastModifiedTime > startTime);
        Assert.Equal(orchestrationA, results[0].LockedBy);
        //Assert.Equal(1, results[0].BacklogQueueSize);     //TODO implement this

        // check that purge on the entity is rejected (because the entity is locked)
        PurgeResult purgeResult = await context.Client.PurgeInstanceAsync(orphanedEntityId.ToString(), context.CancellationToken);        
        Assert.Equal(0, purgeResult.PurgedInstanceCount);

        // check that purge on the orchestration is rejected (because it is not in a completed state)
        purgeResult = await context.Client.PurgeInstanceAsync(orchestrationA.ToString(), context.CancellationToken);
        Assert.Equal(0, purgeResult.PurgedInstanceCount);

        // NOW, we terminate orchestration A, which should implicitly release the lock
        DateTime terminationTime = DateTime.UtcNow;
        await context.Client.TerminateInstanceAsync(orchestrationA, context.CancellationToken);

        // wait for orchestration B to finish
        OrchestrationMetadata metadata = await context.Client.WaitForInstanceCompletionAsync(orchestrationB, getInputsAndOutputs: true, context.CancellationToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());

        // check that orchestration A is reported as terminated
        metadata = await context.Client.WaitForInstanceCompletionAsync(orchestrationA, getInputsAndOutputs: true, context.CancellationToken);
        Assert.Equal(OrchestrationRuntimeStatus.Terminated, metadata.RuntimeStatus);

        // check the status of the entity to confirm that the lock is no longer held
        entityMetadata = await context.Client.Entities.GetEntityAsync(orphanedEntityId, context.CancellationToken);
        Assert.NotNull(entityMetadata);
        Assert.Equal(orphanedEntityId, entityMetadata.Id);
        Assert.True(entityMetadata.IncludesState);
        Assert.Equal(1, entityMetadata.State.ReadAs<int>());
        Assert.True(entityMetadata.LastModifiedTime > terminationTime);
        Assert.Null(entityMetadata.LockedBy);
        Assert.Equal(0, entityMetadata.BacklogQueueSize);

        // same, but using a query
        results = context.Client.Entities.GetAllEntitiesAsync(
           new Microsoft.DurableTask.Client.Entities.EntityQuery()
           {
               InstanceIdStartsWith = orphanedEntityId.ToString(),
               IncludeTransient = false,
               IncludeState = false,
           }).ToBlockingEnumerable().ToList();
        Assert.Single(results);
        Assert.Equal(orphanedEntityId, results[0].Id);
        Assert.False(results[0].IncludesState);
        Assert.True(results[0].LastModifiedTime > terminationTime);
        Assert.Null(results[0].LockedBy);
        Assert.Equal(0, results[0].BacklogQueueSize);

        // purge instances from storage
        purgeResult = await context.Client.PurgeInstanceAsync(orchestrationA);
        Assert.Equal(1, purgeResult.PurgedInstanceCount);
        purgeResult = await context.Client.PurgeInstanceAsync(orchestrationB);
        Assert.Equal(1, purgeResult.PurgedInstanceCount);
        purgeResult = await context.Client.PurgeInstanceAsync(orphanedEntityId.ToString());
        Assert.Equal(1, purgeResult.PurgedInstanceCount);

        // test that purge worked
        purgeResult = await context.Client.PurgeInstanceAsync(orchestrationA);
        Assert.Equal(0, purgeResult.PurgedInstanceCount);
        purgeResult = await context.Client.PurgeInstanceAsync(orchestrationB);
        Assert.Equal(0, purgeResult.PurgedInstanceCount);
        purgeResult = await context.Client.PurgeInstanceAsync(orphanedEntityId.ToString());
        Assert.Equal(0, purgeResult.PurgedInstanceCount);
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<InfiniteLocker>();
        registry.AddOrchestrator<LockingIncrementor>();
    }

    class InfiniteLocker : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
        {
            await using (await context.Entities.LockEntitiesAsync(entityId))
            {
                await context.CreateTimer(DateTime.UtcNow + TimeSpan.FromDays(365), CancellationToken.None);
            }

            // will never reach the end here because we get purged in the middle
            return "ok";
        }
    }

    class LockingIncrementor : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
        {
            await using (await context.Entities.LockEntitiesAsync(entityId))
            {
                await context.Entities.CallEntityAsync(entityId, "increment");

                // we got the entity
                return "ok";
            }
        }
    }
}