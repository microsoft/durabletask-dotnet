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

class TwoCriticalSections : Test
{
    readonly bool sameEntity;

    public TwoCriticalSections(bool sameEntity)
    {
        this.sameEntity = sameEntity;
    }

    public override string Name => $"{base.Name}.{this.sameEntity}";

    public override async Task RunAsync(TestContext context)
    {
        var key1 = Guid.NewGuid().ToString().Substring(0, 8);
        var key2 = this.sameEntity ? key1 : Guid.NewGuid().ToString().Substring(0, 8);

        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(nameof(TwoCriticalSectionsOrchestration), new[] { key1, key2 }, context.CancellationToken);
        OrchestrationMetadata metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, context.CancellationToken);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<TwoCriticalSectionsOrchestration>();
    }

    public class TwoCriticalSectionsOrchestration : TaskOrchestrator<string[], string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string[] entityKeys)
        {
            string key1 = entityKeys![0];
            string key2 = entityKeys![1];

            await using (await context.Entities.LockEntitiesAsync([new EntityInstanceId(nameof(Counter), key1)]))
            {
                await context.Entities.CallEntityAsync(new EntityInstanceId(nameof(Counter), key1), "add", 1);
            }
            await using (await context.Entities.LockEntitiesAsync([new EntityInstanceId(nameof(Counter), key2)]))
            {
                await context.Entities.CallEntityAsync(new EntityInstanceId(nameof(Counter), key2), "add", 1);
            }

            return "ok";
        }
    }
}