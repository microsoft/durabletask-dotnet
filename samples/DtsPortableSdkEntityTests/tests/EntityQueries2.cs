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
using Microsoft.Extensions.Logging;
using Xunit;

namespace DtsPortableSdkEntityTests;

class EntityQueries2 : Test
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

        // ----- next, run a number of orchestrations in order to create and/or delete specific instances
        context.Logger.LogInformation("creating and deleting entities");

        List<string> orchestrations = new List<string>()
        {
            nameof(SignalAndCall.SignalAndCallOrchestration),
            nameof(CallAndDelete.CallAndDeleteOrchestration),
            nameof(SignalAndCall.SignalAndCallOrchestration),
            nameof(CallAndDelete.CallAndDeleteOrchestration),
            nameof(SignalAndCall.SignalAndCallOrchestration),
            nameof(CallAndDelete.CallAndDeleteOrchestration),
            nameof(SignalAndCall.SignalAndCallOrchestration),
            nameof(CallAndDelete.CallAndDeleteOrchestration),
        };

        List<EntityInstanceId> entityIds = new List<EntityInstanceId>()
        {
            new EntityInstanceId("StringStore", "foo"),
            new EntityInstanceId("StringStore2", "bar"),
            new EntityInstanceId("StringStore2", "baz"),
            new EntityInstanceId("StringStore2", "foo"),
            new EntityInstanceId("StringStore2", "ffo"),
            new EntityInstanceId("StringStore2", "zzz"),
            new EntityInstanceId("StringStore2", "aaa"),
            new EntityInstanceId("StringStore2", "bbb"),
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, entityIds.Count),
            context.CancellationToken,
            async (int i, CancellationToken cancellation) =>
            {
                string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrations[i], entityIds[i]);
                await context.Client.WaitForInstanceCompletionAsync(instanceId, cancellation);
            });

        await Task.Delay(TimeSpan.FromSeconds(3)); // accounts for delay in updating instance tables

        // ----- use a collection of (query, validation function) pairs
        context.Logger.LogInformation("starting query tests");

        var tests = new (EntityQuery query, Action<IList<EntityMetadata>> test)[]
        {
            (new EntityQuery
            {
            },
            result =>
            {
                Assert.Equal(4, result.Count());
            }),

            (new EntityQuery
            {
                IncludeTransient = true,
            },
            result =>
            {
                Assert.Equal(context.BackendSupportsImplicitEntityDeletion ? 4 : 8, result.Count());
            }),

            (new EntityQuery
            {
                PageSize = 3,
            },
            result =>
            {
                Assert.Equal(4, result.Count());
            }),

            (new EntityQuery
            {
                IncludeTransient = true,
                PageSize = 3,
            },
            result =>
            {
                Assert.Equal(context.BackendSupportsImplicitEntityDeletion ? 4 : 8, result.Count()); // TODO this is provider-specific
            }),
        };

        foreach (var item in tests)
        {
            List<EntityMetadata> results = new List<EntityMetadata>();
            await foreach (var element in context.Client.Entities.GetAllEntitiesAsync(item.query))
            {
                results.Add(element);
            }

            item.test(results);
        }

        // ----- remove the 4 deleted entities whose metadata still lingers in Azure Storage provider

        context.Logger.LogInformation("starting storage cleaning");

        var cleaningResponse = await context.Client.Entities.CleanEntityStorageAsync();

        Assert.Equal(context.BackendSupportsImplicitEntityDeletion ? 0 : 4, cleaningResponse.EmptyEntitiesRemoved);
        Assert.Equal(0, cleaningResponse.OrphanedLocksReleased);
    }
}