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

class NoOrphanedLockAfterNondeterminism : Test
{

    public override async Task RunAsync(TestContext context)
    {
        DateTime startTime = DateTime.UtcNow;

        // construct unique names for this test
        string prefix = Guid.NewGuid().ToString("N").Substring(0, 6);
        var orphanedEntityId = new EntityInstanceId(nameof(Counter), $"{prefix}-orphaned");
        var orchestrationA = $"{prefix}-A";
        var orchestrationB = $"{prefix}-B";

        // start an orchestration A that acquires the lock and then throws a nondeterminism error
        await context.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(NondeterministicLocker),
            orphanedEntityId,
            new StartOrchestrationOptions() { InstanceId = orchestrationA },
            context.CancellationToken);
        await context.Client.WaitForInstanceStartAsync(orchestrationA, context.CancellationToken);

        // start an orchestration B that queues behind A for the lock
        await context.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(LockingIncrementor2),
            orphanedEntityId,
            new StartOrchestrationOptions() { InstanceId = orchestrationB },
            context.CancellationToken);
        await context.Client.WaitForInstanceStartAsync(orchestrationB, context.CancellationToken);

        // wait for orchestration B to finish
        OrchestrationMetadata metadata = await context.Client.WaitForInstanceCompletionAsync(orchestrationB, getInputsAndOutputs: true, context.CancellationToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());

        // check that orchestration A reported nondeterminism
        metadata = await context.Client.WaitForInstanceCompletionAsync(orchestrationA, getInputsAndOutputs: true, context.CancellationToken);
        Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
        Assert.Contains("Non-Deterministic workflow detected", metadata.FailureDetails?.ErrorMessage);

        // check the status of the entity to confirm that the lock is no longer held
        EntityMetadata? entityMetadata = await context.Client.Entities.GetEntityAsync(orphanedEntityId, context.CancellationToken);
        Assert.NotNull(entityMetadata);
        Assert.Equal(orphanedEntityId, entityMetadata.Id);
        Assert.True(entityMetadata.IncludesState);
        Assert.Equal(1, entityMetadata.State.ReadAs<int>());
        Assert.True(entityMetadata.LastModifiedTime > startTime);
        Assert.Null(entityMetadata.LockedBy);
        Assert.Equal(0, entityMetadata.BacklogQueueSize);

        // purge instances from storage
        PurgeResult purgeResult = await context.Client.PurgeInstanceAsync(orchestrationA);
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
        registry.AddOrchestrator<NondeterministicLocker>();
        registry.AddOrchestrator<LockingIncrementor2>();
    }

    class NondeterministicLocker : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
        {
            if (!context.IsReplaying)  // replay will encounter nondeterminism before replaying the lock
            {
                await context.Entities.LockEntitiesAsync(entityId);
            }

            return "nondeterminstic";
        }
    }

    class LockingIncrementor2 : TaskOrchestrator<EntityInstanceId, string>
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