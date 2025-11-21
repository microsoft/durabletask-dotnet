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
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DtsPortableSdkEntityTests;

class FaultyCriticalSection : Test
{
    public override async Task RunAsync(TestContext context)
    {
        var entityId = new EntityInstanceId(nameof(Counter), Guid.NewGuid().ToString());
        string orchestrationName = nameof(FaultyCriticalSectionOrchestration);

        // run the critical section but fail in the middle
        {
            string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, new FaultyCriticalSectionOrchestration.Input(entityId, true));
            var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs:true);
            Assert.Equal(OrchestrationRuntimeStatus.Failed, metadata.RuntimeStatus);
            Assert.NotNull(metadata.FailureDetails);
            Assert.Equal("KABOOM", metadata.FailureDetails.ErrorMessage);
        }

        // run the critical section again without failing this time - this will time out if lock was not released properly.
        {
            string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, new FaultyCriticalSectionOrchestration.Input(entityId, false));
            var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
            Assert.Equal("ok", metadata.ReadOutputAs<string>());
        }
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<FaultyCriticalSectionOrchestration>();
    }
}

class FaultyCriticalSectionOrchestration : TaskOrchestrator<FaultyCriticalSectionOrchestration.Input,string>
{
    public record Input(EntityInstanceId EntityInstanceId, bool Fail);

    public override async Task<string> RunAsync(TaskOrchestrationContext context, Input input)
    {
        await using (await context.Entities.LockEntitiesAsync(input.EntityInstanceId))
        {
            if (input.Fail)
            {
                throw new Exception("KABOOM");
            }
        }

        return "ok";
    }
}