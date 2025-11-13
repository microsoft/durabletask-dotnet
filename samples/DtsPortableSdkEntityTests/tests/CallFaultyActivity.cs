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

class CallFaultyActivity : Test
{
    // this is not an entity test... but it's a good place to put this test

    private readonly bool nested;

    public CallFaultyActivity(bool nested)
    {
        this.nested = nested;
    }
    public override string Name => $"{base.Name}.{(this.nested ? "Nested" : "NotNested")}";

    public override async Task RunAsync(TestContext context)
    {
        string orchestrationName = nameof(CallFaultyActivityOrchestration);
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, this.nested);
        var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());
    }

    public override void Register(DurableTaskRegistry registry, IServiceCollection services)
    {
        registry.AddActivity<FaultyActivity>();
        registry.AddOrchestrator<CallFaultyActivityOrchestration>();
    }
}

class FaultyActivity : TaskActivity<bool, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, bool nested)
    {
        if (!nested)
        {
            this.MethodThatThrowsException();
        }
        else
        {
            this.MethodThatThrowsNestedException();
        }

        return Task.FromResult("unreachable");
    }

    public void MethodThatThrowsNestedException()
    {
        try
        {
            this.MethodThatThrowsException();
        }
        catch (Exception e)
        {
            throw new Exception("KABOOOOOM", e);
        }
    }

    public void MethodThatThrowsException()
    {
        throw new Exception("KABOOM");
    }
}

class CallFaultyActivityOrchestration : TaskOrchestrator<bool, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, bool nested)
    {
        try
        {
            await context.CallActivityAsync(nameof(FaultyActivity), nested);
            throw new Exception("expected activity to throw exception, but none was thrown");
        }
        catch (TaskFailedException taskFailedException)
        {
            Assert.NotNull(taskFailedException.FailureDetails);

            if (!nested)
            {
                Assert.Equal("KABOOM", taskFailedException.FailureDetails.ErrorMessage);
                Assert.Contains(nameof(FaultyActivity.MethodThatThrowsException), taskFailedException.FailureDetails.StackTrace);
            }
            else
            {
                Assert.Equal("KABOOOOOM", taskFailedException.FailureDetails.ErrorMessage);
                Assert.Contains(nameof(FaultyActivity.MethodThatThrowsNestedException), taskFailedException.FailureDetails.StackTrace);

                Assert.NotNull(taskFailedException.FailureDetails.InnerFailure);
                Assert.Equal("KABOOM", taskFailedException.FailureDetails.InnerFailure!.ErrorMessage);
                Assert.Contains(nameof(FaultyActivity.MethodThatThrowsException), taskFailedException.FailureDetails.InnerFailure.StackTrace);
            }
        }
        catch (Exception e)
        {
            throw new Exception($"wrong exception thrown", e);
        }

        return "ok";
    }
}
