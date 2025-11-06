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

class CallSingleFaultyEntity : Test
{
    public override async Task RunAsync(TestContext context)
    {
        var entityId = new EntityInstanceId(nameof(FaultyEntity), Guid.NewGuid().ToString());
        string orchestrationName = nameof(CallFaultyEntityOrchestration);
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, entityId);
        var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddOrchestrator<CallFaultyEntityOrchestration>();
    }
}

class CallFaultyEntityOrchestration : TaskOrchestrator<EntityInstanceId, string>
{     
    public override async Task<string> RunAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
    {
        async Task ExpectOperationExceptionAsync(Task t, EntityInstanceId entityId, string operationName,
                            string errorMessage, string? errorMethod = null, string? innerErrorMessage = null, string innerErrorMethod = "")
        {
            try
            {
                await t;
                throw new Exception("expected operation exception, but none was thrown");
            }
            catch (EntityOperationFailedException entityException)
            {
                Assert.Equal(operationName, entityException.OperationName);
                Assert.Equal(entityId, entityException.EntityId);
                Assert.Contains(errorMessage, entityException.Message);

                Assert.NotNull(entityException.FailureDetails);
                Assert.Equal(errorMessage, entityException.FailureDetails.ErrorMessage);

                if (errorMethod != null)
                {
                    Assert.Contains(errorMethod, entityException.FailureDetails.StackTrace);
                }

                if (innerErrorMessage != null)
                {
                    Assert.NotNull(entityException.FailureDetails.InnerFailure);
                    Assert.Equal(innerErrorMessage, entityException.FailureDetails.InnerFailure!.ErrorMessage);

                    if (innerErrorMethod != null)
                    {
                        Assert.Contains(innerErrorMethod, entityException.FailureDetails.InnerFailure.StackTrace);
                    }
                }
                else
                {
                    Assert.Null(entityException.FailureDetails.InnerFailure);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"wrong exception thrown", e);
            }
        }

        try
        {
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ExpectOperationExceptionAsync(
                context.Entities.CallEntityAsync(entityId, "Throw"),
                entityId,
                "Throw",
                "KABOOM",
                "ThrowTestException");

            await ExpectOperationExceptionAsync(
                context.Entities.CallEntityAsync(entityId, "ThrowNested"),
                entityId,
                "ThrowNested",
                "KABOOOOOM",
                "FaultyEntity.RunAsync",
                "KABOOM",
                "ThrowTestException");

            await ExpectOperationExceptionAsync(
                context.Entities.CallEntityAsync(entityId, "SetToUnserializable"),
                entityId,
                "SetToUnserializable",
                "problematic object: is not serializable",
                "ProblematicObjectJsonConverter.Write");

            // since the operations failed, the entity state is unchanged, meaning the entity still does not exist
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await context.Entities.CallEntityAsync(entityId, "SetToUndeserializable");

            Assert.True(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ExpectOperationExceptionAsync(
               context.Entities.CallEntityAsync<int>(entityId, "Get"),
               entityId,
               "Get",
               "problematic object: is not deserializable",
               "ProblematicObjectJsonConverter.Read");

            await context.Entities.CallEntityAsync(entityId, "DeleteWithoutReading");

            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await context.Entities.CallEntityAsync(entityId, "Set", 3);

            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ExpectOperationExceptionAsync(
               context.Entities.CallEntityAsync(entityId, "SetThenThrow", 333),
               entityId,
               "SetThenThrow",
               "KABOOM",
               "FaultyEntity.RunAsync");


            // value should be unchanged
            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ExpectOperationExceptionAsync(
              context.Entities.CallEntityAsync(entityId, "DeleteThenThrow"),
              entityId,
              "DeleteThenThrow",
              "KABOOM",
               "FaultyEntity.RunAsync");

            // value should be unchanged
            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await context.Entities.CallEntityAsync(entityId, "Delete");

            // entity was deleted
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ExpectOperationExceptionAsync(
              context.Entities.CallEntityAsync(entityId, "SetThenThrow", 333),
              entityId,
              "SetThenThrow",
              "KABOOM",
               "FaultyEntity.RunAsync");

            // must have rolled back to non-existing state
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            return "ok";
        }
        catch (Exception e)
        {
            return e.ToString();
        }
    }
}