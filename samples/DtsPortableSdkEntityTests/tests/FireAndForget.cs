// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Xunit;

namespace DtsPortableSdkEntityTests;

/// <summary>
/// Scenario that starts a new orchestration from an entity.
/// </summary>
class FireAndForget : Test
{
    private readonly int? delay;

    public FireAndForget(int? delay)
    {
        this.delay = delay;
    }

    public override string Name => $"{base.Name}.{(this.delay.HasValue ? "Delay" + this.delay.Value.ToString() : "NoDelay")}";

    public override async Task RunAsync(TestContext context)
    {
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(nameof(LaunchOrchestrationFromEntity), this.delay, context.CancellationToken);
        OrchestrationMetadata metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true, context.CancellationToken);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);

        string? signallingOrchestrationInstanceId = metadata.ReadOutputAs<string>();
        Assert.NotNull(signallingOrchestrationInstanceId);
        var launchedMetadata = await context.Client.GetInstanceAsync(signallingOrchestrationInstanceId!, getInputsAndOutputs: true, context.CancellationToken);
        Assert.NotNull(launchedMetadata);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, launchedMetadata!.RuntimeStatus);
        Assert.Equal("ok", launchedMetadata!.ReadOutputAs<string>());
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<LaunchOrchestrationFromEntity>();
        registry.AddOrchestrator<SignallingOrchestration>();

    }

    public class LaunchOrchestrationFromEntity : TaskOrchestrator<int?, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, int? delay)
        {
            var entityId = new EntityInstanceId("Launcher", context.NewGuid().ToString().Substring(0, 8));

            if (delay.HasValue)
            {
                await context.Entities.CallEntityAsync(entityId, "launch", context.CurrentUtcDateTime + TimeSpan.FromSeconds(delay.Value));
            }
            else
            {
                await context.Entities.CallEntityAsync(entityId, "launch");
            }

            while (true)
            {
                string? signallingOrchestrationId = await context.Entities.CallEntityAsync<string>(entityId, "get");

                if (signallingOrchestrationId != null)
                {
                    return signallingOrchestrationId;
                }

                await context.CreateTimer(DateTime.UtcNow + TimeSpan.FromSeconds(1), CancellationToken.None);
            }
        }
    }

    public class SignallingOrchestration : TaskOrchestrator<EntityInstanceId, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
        {
            await context.CreateTimer(DateTime.UtcNow + TimeSpan.FromSeconds(.2), CancellationToken.None);

            await context.Entities.SignalEntityAsync(entityId, "done");

            // to test replay, we add a little timer
            await context.CreateTimer(DateTime.UtcNow + TimeSpan.FromMilliseconds(1), CancellationToken.None);

            return "ok";
        }
    }
}