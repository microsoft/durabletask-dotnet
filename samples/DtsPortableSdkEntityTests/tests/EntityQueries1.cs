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

class EntityQueries1 : Test
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

        var yesterday = DateTime.UtcNow.Subtract(TimeSpan.FromDays(1));
        var tomorrow = DateTime.UtcNow.Add(TimeSpan.FromDays(1));

        // ----- next, run a number of orchestrations in order to create specific instances
        context.Logger.LogInformation("creating entities");

        List<EntityInstanceId> entityIds = new List<EntityInstanceId>()
        {
            new EntityInstanceId("StringStore", "foo"),
            new EntityInstanceId("StringStore", "bar"),
            new EntityInstanceId("StringStore", "baz"),
            new EntityInstanceId("StringStore2", "foo"),
        };
        
        await Parallel.ForEachAsync(
            Enumerable.Range(0, entityIds.Count), 
            context.CancellationToken, 
            async (int i, CancellationToken cancellation) =>
            {
                string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(nameof(SignalAndCall.SignalAndCallOrchestration), entityIds[i]);
                await context.Client.WaitForInstanceCompletionAsync(instanceId, cancellation);
            });

        // ----- to more easily read this, we first create a collection of (query, validation function) pairs
        context.Logger.LogInformation("starting query tests");

        var tests = new (EntityQuery query, Action<IList<EntityMetadata>> test)[]
        {
            (new EntityQuery
            {
                InstanceIdStartsWith = "StringStore",
            },
            result =>
            {
                Assert.Equal(4, result.Count());
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore",
            },
            result =>
            {
                Assert.Equal(4, result.Count());
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@stringstore",
            },
            result =>
            {
                Assert.Equal(4, result.Count());
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore@",
            },
            result =>
            {
                Assert.Equal(3, result.Count());
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "StringStore@",
            },
            result =>
            {
                Assert.Equal(3, result.Count());
            }),


            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore@foo",
            },
            result =>
            {
                Assert.Single(result);
                Assert.True(result[0].IncludesState);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore@foo",
                IncludeState = false,
            },
            result =>
            {
                Assert.Single(result);
                Assert.False(result[0].IncludesState);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore@",
                LastModifiedFrom = yesterday,
                LastModifiedTo = tomorrow,
            },
            result =>
            {
                Assert.Equal(3, result.Count);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "StringStore@ba",
                LastModifiedFrom = yesterday,
                LastModifiedTo = tomorrow,
            },
            result =>
            {
                Assert.Equal(2, result.Count);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "stringstore@BA",
                LastModifiedFrom = yesterday,
                LastModifiedTo = tomorrow,
            },
            result =>
            {
                Assert.Empty(result);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore@ba",
                LastModifiedFrom = yesterday,
                LastModifiedTo = tomorrow,
            },
            result =>
            {
                Assert.Equal(2, result.Count);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@stringstore@BA",
                LastModifiedFrom = yesterday,
                LastModifiedTo = tomorrow,
            },
            result =>
            {
                Assert.Empty(result);
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@StringStore@",
                PageSize = 2,
            },
            result =>
            {
                 Assert.Equal(3, result.Count());
            }),

            (new EntityQuery
            {
                InstanceIdStartsWith = "@noResult",
                LastModifiedFrom = yesterday,
                LastModifiedTo = tomorrow,
            },
            result =>
            {
                Assert.Empty(result);
            }),

            (new EntityQuery
            {
                LastModifiedFrom = tomorrow,
            },
            result =>
            {
                Assert.Empty(result);
            }),

            (new EntityQuery
            {
                LastModifiedTo = yesterday,
            },
            result =>
            {
                Assert.Empty(result);
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
    }
}