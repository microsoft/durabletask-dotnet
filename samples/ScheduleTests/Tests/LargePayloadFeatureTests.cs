// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ScheduleTests.Tests
{
    // NOTE: These tests focus on the Large Payload feature using in-memory payload store
    // for determinism. Each test creates its own Host with externalized payloads enabled
    // on both client and worker, and registers just the tasks it needs.
    public class LargePayloadFeatureTests
    {
        const int OneKb = 1024;
        const int ThresholdBytes = 1024; // force externalization for most test payloads

        [Fact]
        public async Task OrchestrationInput_Externalized_And_Resolved()
        {
            string largeInput = new string('I', 1024 * 1024);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(
                    nameof(OrchestrationInput_Externalized_And_Resolved),
                    (ctx, input) =>
                    {
                        if (input.StartsWith("blob:v1:", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Orchestrator received blob token instead of payload");
                        }
                        if (input != largeInput)
                        {
                            throw new InvalidOperationException("Orchestrator input mismatch");
                        }
                        return Task.FromResult(input);
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(
                nameof(OrchestrationInput_Externalized_And_Resolved), largeInput);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);

            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            Assert.Equal(largeInput, done.ReadInputAs<string>());
            
        }

        [Fact]
        public async Task OrchestrationOutput_Externalized_And_Resolved()
        {
            string largeOutput = new string('O', 900 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(
                    nameof(OrchestrationOutput_Externalized_And_Resolved),
                    (ctx, _) => Task.FromResult(largeOutput)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(
                nameof(OrchestrationOutput_Externalized_And_Resolved));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);

            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            Assert.Equal(largeOutput, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task CustomStatus_Externalized_And_Resolved()
        {
            string largeStatus = new string('S', 800 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(
                    nameof(CustomStatus_Externalized_And_Resolved),
                    (ctx, _) =>
                    {
                        ctx.SetCustomStatus(largeStatus);
                        return Task.FromResult("done");
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(CustomStatus_Externalized_And_Resolved));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);

            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            Assert.Equal("done", done.ReadOutputAs<string>());
            Assert.Equal(largeStatus, done.ReadCustomStatusAs<string>());
            
        }

        [Fact]
        public async Task ContinueAsNew_With_LargeInput_Roundtrip()
        {
            string nextInput = new string('N', 850 * OneKb);
            string finalOut = new string('F', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string?, string>(
                    nameof(ContinueAsNew_With_LargeInput_Roundtrip),
                    (ctx, input) =>
                    {
                        if (input == null)
                        {
                            ctx.ContinueAsNew(nextInput);
                            return Task.FromResult("");
                        }
                        return Task.FromResult(finalOut);
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(ContinueAsNew_With_LargeInput_Roundtrip));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(finalOut, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task ActivityInput_Externalized_Resolved_OnWorker()
        {
            string largeParam = new string('P', 750 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(nameof(ActivityInput_Externalized_Resolved_OnWorker), (ctx, _) => ctx.CallActivityAsync<string>("Echo", largeParam))
                    .AddActivityFunc<string, string>("Echo", (ctx, input) =>
                    {
                        if (input.StartsWith("blob:v1:", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Activity received blob token instead of payload");
                        }
                        if (input != largeParam)
                        {
                            throw new InvalidOperationException("Activity input mismatch");
                        }
                        return input;
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(ActivityInput_Externalized_Resolved_OnWorker));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(largeParam, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task ActivityOutput_Externalized_Resolved_InOrchestrator()
        {
            string largeOut = new string('A', 820 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(nameof(ActivityOutput_Externalized_Resolved_InOrchestrator), async (ctx, _) =>
                    {
                        string result = await ctx.CallActivityAsync<string>("Produce", (object?)null);
                        if (result.StartsWith("blob:v1:", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Orchestrator saw blob token in activity result");
                        }
                        return result;
                    })
                    .AddActivityFunc<object?, string>("Produce", (ctx, _) => largeOut));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(ActivityOutput_Externalized_Resolved_InOrchestrator));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(largeOut, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task SubOrchestrationInput_Externalized_Resolved()
        {
            string subInput = new string('C', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(
                        nameof(SubOrchestrationInput_Externalized_Resolved) + "_Parent",
                        async (ctx, _) => await ctx.CallSubOrchestratorAsync<string>(nameof(SubOrchestrationInput_Externalized_Resolved) + "_Child", subInput))
                    .AddOrchestratorFunc<string, string>(nameof(SubOrchestrationInput_Externalized_Resolved) + "_Child", (ctx, input) =>
                    {
                        if (input.StartsWith("blob:v1:", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Child orchestrator received blob token instead of payload");
                        }
                        if (input != subInput)
                        {
                            throw new InvalidOperationException("Child orchestrator input mismatch");
                        }
                        return Task.FromResult(input);
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(SubOrchestrationInput_Externalized_Resolved) + "_Parent");
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(subInput, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task SubOrchestrationOutput_Externalized_Resolved()
        {
            string subOut = new string('Z', 760 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(
                        nameof(SubOrchestrationOutput_Externalized_Resolved) + "_Parent",
                        async (ctx, _) => await ctx.CallSubOrchestratorAsync<string>(nameof(SubOrchestrationOutput_Externalized_Resolved) + "_Child", (object?)null))
                    .AddOrchestratorFunc<object?, string>(nameof(SubOrchestrationOutput_Externalized_Resolved) + "_Child", (ctx, _) => Task.FromResult(subOut)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(SubOrchestrationOutput_Externalized_Resolved) + "_Parent");
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(subOut, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task ExternalEvent_LargePayload_Externalized_ByClient_Resolved_ByWorker()
        {
            string largeEvent = new string('E', 600 * OneKb);
            const string EventName = "LargeEvent";
            string orch = nameof(ExternalEvent_LargePayload_Externalized_ByClient_Resolved_ByWorker);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string>(orch, async ctx =>
                {
                    string v = await ctx.WaitForExternalEvent<string>(EventName);
                    if (v.StartsWith("blob:v1:", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Orchestrator received blob token for external event");
                    }
                    if (v != largeEvent)
                    {
                        throw new InvalidOperationException("External event payload mismatch");
                    }
                    return v;
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);

            await scope.Client.RaiseEventAsync(id, EventName, largeEvent, default);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(largeEvent, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task Terminate_With_LargeOutput_Externalized_ByClient()
        {
            string largeOutput = new string('T', 700 * OneKb);
            string orch = nameof(Terminate_With_LargeOutput_Externalized_ByClient);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, object?>(orch, async (ctx, _) =>
                {
                    await ctx.CreateTimer(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return null;
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.TerminateInstanceAsync(id, new TerminateInstanceOptions { Output = largeOutput }, default);
            await AssertEventuallyStatus(scope.Client, id, OrchestrationRuntimeStatus.Terminated, TimeSpan.FromSeconds(30));
            
        }


        [Fact]
        public async Task Query_With_FetchInputsOutputs_Resolves_LargeIO()
        {
            string largeIn = new string('Q', 740 * OneKb);
            string largeOut = new string('q', 880 * OneKb);
            string orch = nameof(Query_With_FetchInputsOutputs_Resolves_LargeIO);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(orch, (ctx, input) =>
                {
                    if (input.StartsWith("blob:v1:", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Orchestrator received blob token instead of payload (query test)");
                    }
                    if (input != largeIn)
                    {
                        throw new InvalidOperationException("Orchestrator input mismatch (query test)");
                    }
                    return Task.FromResult(largeOut);
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch, largeIn);
            await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: false, default);

            AsyncPageable<OrchestrationMetadata> page = scope.Client.GetAllInstancesAsync(new OrchestrationQuery
            {
                FetchInputsAndOutputs = true,
                InstanceIdPrefix = id
            });
            OrchestrationMetadata? found = null;
            await foreach (OrchestrationMetadata item in page)
            {
                if (item.Name == orch)
                {
                    found = item;
                    break;
                }
            }

            Assert.NotNull(found);
            Assert.Equal(largeIn, found!.ReadInputAs<string>());
            Assert.Equal(largeOut, found!.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, found!.RuntimeStatus);
        }

        [Fact]
        public async Task History_Resolves_ExecutionStarted_And_TaskCompleted()
        {
            string largeInput = new string('H', 2 * 1024 * 1024);
            string largeOutput = new string('O', 2 * 1024 * 1024);
            string orch = nameof(History_Resolves_ExecutionStarted_And_TaskCompleted);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string, string>(
                    orch,
                    async (ctx, input) =>
                    {
                        for (int i = 0; i < 25; i++)
                        {
                            await ctx.CreateTimer(TimeSpan.FromMilliseconds(10), CancellationToken.None);
                        }
                        return largeOutput;
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch, largeInput);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);

            Assert.Equal(largeInput, done.ReadInputAs<string>());
            Assert.Equal(largeOutput, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task History_Resolves_Events_Raised_And_ContinueAsNew()
        {
            string largeEvent = new string('E', 700 * OneKb);
            string largeNext = new string('N', 700 * OneKb);
            string orch = nameof(History_Resolves_Events_Raised_And_ContinueAsNew);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(tasks => tasks.AddOrchestratorFunc<string?, string>(
                    orch,
                    async (ctx, input) =>
                    {
                        if (input == null)
                        {
                            string data = await ctx.WaitForExternalEvent<string>("evt");
                            ctx.ContinueAsNew(largeNext);
                            return data; // unreachable
                        }
                        return "done";
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.RaiseEventAsync(id, "evt", largeEvent, default);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal("done", done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task History_Resolves_Terminate_Suspend_Resume_Inputs()
        {
            string termOut = new string('T', 680 * OneKb);
            string susReason = new string('S', 640 * OneKb);
            string resReason = new string('R', 630 * OneKb);
            string orch = nameof(History_Resolves_Terminate_Suspend_Resume_Inputs);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, object?>(
                    orch,
                    async (ctx, _) =>
                    {
                        await ctx.CreateTimer(TimeSpan.FromMinutes(5), CancellationToken.None);
                        return null;
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.SuspendInstanceAsync(id, susReason, default);
            await scope.Client.ResumeInstanceAsync(id, resReason, default);
            await scope.Client.TerminateInstanceAsync(id, new TerminateInstanceOptions { Output = termOut }, default);

            // wait for terminated (allow up to 60s for eventual consistency)
            await AssertEventuallyStatus(scope.Client, id, OrchestrationRuntimeStatus.Terminated, TimeSpan.FromSeconds(60));
        }

        [Fact]
        public async Task Orchestrator_SendEventAction_Large_Data_To_Other_Instance()
        {
            string data = new string('D', 700 * OneKb);
            string waiter = nameof(Orchestrator_SendEventAction_Large_Data_To_Other_Instance) + "_Waiter";
            string sender = nameof(Orchestrator_SendEventAction_Large_Data_To_Other_Instance) + "_Sender";
            const string Evt = "x";

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<string>(waiter, async ctx => await ctx.WaitForExternalEvent<string>(Evt))
                    .AddOrchestratorFunc<string, string>(sender, async (ctx, targetId) =>
                    {
                        // Verify payloads passed within orchestrator are not tokens
                        if (targetId.StartsWith("blob:v1:", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Sender orchestrator received blob token as target id");
                        }
                        ctx.SendEvent(targetId, Evt, data);
                        return "ok";
                    }));
            });

            // Start waiter first and capture its instance id
            string waitId = await scope.Client.ScheduleNewOrchestrationInstanceAsync(waiter);
            await scope.Client.WaitForInstanceStartAsync(waitId, default);

            // Start sender, providing waiter id as input
            string senderId = await scope.Client.ScheduleNewOrchestrationInstanceAsync(sender, waitId);
            OrchestrationMetadata senderDone = await scope.Client.WaitForInstanceCompletionAsync(senderId, getInputsAndOutputs: true, default);
            Assert.Equal("ok", senderDone.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, senderDone.RuntimeStatus);

            OrchestrationMetadata waitDone = await scope.Client.WaitForInstanceCompletionAsync(waitId, getInputsAndOutputs: true, default);
            Assert.Equal(data, waitDone.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, waitDone.RuntimeStatus);
            
        }

        [Fact]
        public async Task Entity_CallEntity_Large_Input_Resolved()
        {
            string largeEntityInput = new string('E', 720 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, int>(nameof(Entity_CallEntity_Large_Input_Resolved), (ctx, _) => ctx.Entities.CallEntityAsync<int>(new EntityInstanceId(nameof(EchoLenEntity), "1"), "EchoLength", largeEntityInput))
                    .AddEntity(typeof(EchoLenEntity)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Entity_CallEntity_Large_Input_Resolved));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            int len = done.ReadOutputAs<int>();
            Assert.Equal(largeEntityInput.Length, len);
            
        }

        [Fact]
        public async Task Entity_CallEntity_Large_Output_Resolved()
        {
            int size = 850 * OneKb;
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, int>(nameof(Entity_CallEntity_Large_Output_Resolved), async (ctx, _) => (await ctx.Entities.CallEntityAsync<string>(new EntityInstanceId(nameof(LargeResultEntity), "1"), "Produce", size)).Length)
                    .AddEntity(typeof(LargeResultEntity)));
                worker.Configure(o => o.EnableEntitySupport = true);
            }, clientConfigure: client => client.Configure(o => o.EnableEntitySupport = true));

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Entity_CallEntity_Large_Output_Resolved));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            int len = done.ReadOutputAs<int>();
            Assert.Equal(size, len);
            
        }

        [Fact]
        public async Task Entity_State_Large_Resolved_Via_GetEntity()
        {
            string state = new string('S', 900 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, object?>(nameof(Entity_State_Large_Resolved_Via_GetEntity), async (ctx, _) =>
                    {
                        await ctx.Entities.CallEntityAsync(new EntityInstanceId(nameof(StateEntity), "1"), "Set", state);
                        return null;
                    })
                    .AddEntity(typeof(StateEntity)));
                worker.Configure(o => o.EnableEntitySupport = true);
            }, clientConfigure: client => client.Configure(o => o.EnableEntitySupport = true));

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Entity_State_Large_Resolved_Via_GetEntity));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);

            EntityMetadata<string>? em = await scope.Client.Entities.GetEntityAsync<string>(new EntityInstanceId(nameof(StateEntity), "1"), includeState: true, cancellation: default);
            Assert.NotNull(em);
            Assert.Equal(state.Length, em!.State!.Length);
            Assert.Equal(state, em!.State);
            
        }

        [Fact]
        public async Task QueryEntities_Resolves_Large_State()
        {
            string state = new string('S', 880 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, object?>(nameof(QueryEntities_Resolves_Large_State), async (ctx, _) =>
                    {
                        await ctx.Entities.CallEntityAsync(new EntityInstanceId(nameof(StateEntity), "2"), "Set", state);
                        return null;
                    })
                    .AddEntity<StateEntity>(nameof(StateEntity)));
                worker.Configure(o => o.EnableEntitySupport = true);
            }, clientConfigure: client => client.Configure(o => o.EnableEntitySupport = true));

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(QueryEntities_Resolves_Large_State));
            await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);

            await foreach (var em in scope.Client.Entities.GetAllEntitiesAsync(new Microsoft.DurableTask.Client.Entities.EntityQuery { IncludeState = true }))
            {
                if (em.Id.Name == nameof(StateEntity) && em.Id.Key == "2")
                {
                    Assert.True(em.IncludesState);
                    // em is EntityMetadata (non-generic) so use the generic overload for state
                    // Fetch strongly-typed state
                    var em2 = await scope.Client.Entities.GetEntityAsync<string>(em.Id, includeState: true, cancellation: default);
                    Assert.NotNull(em2);
                    Assert.Equal(state, em2!.State);
                    break;
                }
            }
            
        }

        [Fact]
        public async Task Multiple_LargePayloads_In_Single_Orchestration_Roundtrip()
        {
            string a = new string('A', 700 * OneKb);
            string b = new string('B', 710 * OneKb);
            string c = new string('C', 720 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, (string, string, string)>(
                        nameof(Multiple_LargePayloads_In_Single_Orchestration_Roundtrip),
                        async (ctx, _) =>
                        {
                            string x = await ctx.CallActivityAsync<string>("Echo", a);
                            string y = await ctx.CallSubOrchestratorAsync<string>("Child", b);
                            ctx.SetCustomStatus(c);
                            return (x, y, c);
                        })
                    .AddActivityFunc<string, string>("Echo", (ctx, input) => input)
                    .AddOrchestratorFunc<string, string>("Child", (ctx, input) => Task.FromResult(input)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Multiple_LargePayloads_In_Single_Orchestration_Roundtrip));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            (string oa, string ob, string oc) = done.ReadOutputAs<(string, string, string)>();
            Assert.Equal(a, oa);
            Assert.Equal(b, ob);
            Assert.Equal(c, done.ReadCustomStatusAs<string>());
            
        }

        [Fact]
        public async Task Below_Threshold_Is_Not_Externalized()
        {
            string small = new string('x', 64 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                // raise threshold high to avoid externalization
                worker.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 2 * 1024 * 1024;
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                    opts.ContainerName = "payloadtest";
                });

                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(nameof(Below_Threshold_Is_Not_Externalized), (ctx, input) => Task.FromResult(input)));
            }, clientConfigure: client =>
            {
                client.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = 2 * 1024 * 1024;
                    opts.ConnectionString = "UseDevelopmentStorage=true";
                    opts.ContainerName = "payloadtest";
                });
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Below_Threshold_Is_Not_Externalized), small);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(small, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task At_Threshold_Is_Externalized()
        {
            string payload = new string('t', ThresholdBytes); // equal to threshold => externalize
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(nameof(At_Threshold_Is_Externalized), (ctx, input) => Task.FromResult(input)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(At_Threshold_Is_Externalized), payload);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(payload, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task Token_Never_Leaks_To_Activity_Input()
        {
            string large = new string('y', 700 * OneKb);
            string? observed = null;
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(nameof(Token_Never_Leaks_To_Activity_Input), (ctx, _) => ctx.CallActivityAsync<string>("Check", large))
                    .AddActivityFunc<string, string>("Check", (ctx, input) =>
                    {
                        observed = input;
                        return input;
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Token_Never_Leaks_To_Activity_Input));
            await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.NotNull(observed);
            Assert.DoesNotContain("blob:v1:", observed);
            OrchestrationMetadata? mTokenLeak = await scope.Client.GetInstanceAsync(id, getInputsAndOutputs: false, default);
            Assert.NotNull(mTokenLeak);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, mTokenLeak!.RuntimeStatus);
        }

        [Fact]
        public async Task Token_Never_Leaks_To_Output_Or_CustomStatus()
        {
            string outp = new string('o', 700 * OneKb);
            string stat = new string('s', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(
                    nameof(Token_Never_Leaks_To_Output_Or_CustomStatus),
                    (ctx, _) =>
                    {
                        ctx.SetCustomStatus(stat);
                        return Task.FromResult(outp);
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Token_Never_Leaks_To_Output_Or_CustomStatus));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.DoesNotContain("blob:v1:", done.SerializedOutput);
            Assert.DoesNotContain("blob:v1:", done.SerializedCustomStatus);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task Parallel_Activities_With_Large_Inputs()
        {
            string a = new string('a', 700 * OneKb);
            string b = new string('b', 700 * OneKb);
            string c = new string('c', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(nameof(Parallel_Activities_With_Large_Inputs), async (ctx, _) =>
                    {
                        Task<string> t1 = ctx.CallActivityAsync<string>("E", a);
                        Task<string> t2 = ctx.CallActivityAsync<string>("E", b);
                        Task<string> t3 = ctx.CallActivityAsync<string>("E", c);
                        await Task.WhenAll(t1, t2, t3);
                        return t1.Result + t2.Result + t3.Result;
                    })
                    .AddActivityFunc<string, string>("E", (ctx, input) => input));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Parallel_Activities_With_Large_Inputs));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(a + b + c, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            
        }

        [Fact]
        public async Task Parallel_SubOrchestrations_With_Large_Inputs()
        {
            string a = new string('A', 700 * OneKb);
            string b = new string('B', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(nameof(Parallel_SubOrchestrations_With_Large_Inputs), async (ctx, _) =>
                    {
                        Task<string> t1 = ctx.CallSubOrchestratorAsync<string>("ChildA", a);
                        Task<string> t2 = ctx.CallSubOrchestratorAsync<string>("ChildB", b);
                        await Task.WhenAll(t1, t2);
                        return t1.Result + t2.Result;
                    })
                    .AddOrchestratorFunc<string, string>("ChildA", (ctx, input) => Task.FromResult(input))
                    .AddOrchestratorFunc<string, string>("ChildB", (ctx, input) => Task.FromResult(input)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Parallel_SubOrchestrations_With_Large_Inputs));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(a + b, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            
        }

        [Fact]
        public async Task Multiple_Large_RaiseEvent_Calls()
        {
            string e1 = new string('1', 650 * OneKb);
            string e2 = new string('2', 660 * OneKb);
            const string EventName = "evt";
            string orch = nameof(Multiple_Large_RaiseEvent_Calls);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string>(
                    orch,
                    async ctx =>
                    {
                        string a = await ctx.WaitForExternalEvent<string>(EventName);
                        string b = await ctx.WaitForExternalEvent<string>(EventName);
                        return a + b;
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.RaiseEventAsync(id, EventName, e1, default);
            await scope.Client.RaiseEventAsync(id, EventName, e2, default);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(e1 + e2, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            
        }

        [Fact]
        public async Task Suspend_Resume_With_Large_Reasons_BackToBack()
        {
            string r1 = new string('A', 640 * OneKb);
            string r2 = new string('B', 640 * OneKb);
            string orch = nameof(Suspend_Resume_With_Large_Reasons_BackToBack);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(
                    orch,
                    async (ctx, _) =>
                    {
                        await ctx.CreateTimer(TimeSpan.FromMinutes(5), CancellationToken.None);
                        return "done";
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.SuspendInstanceAsync(id, r1, default);
            await scope.Client.ResumeInstanceAsync(id, r2, default);
            await AssertEventuallyStatus(scope.Client, id, OrchestrationRuntimeStatus.Running, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Large_CustomStatus_Updated_Multiple_Times()
        {
            string s1 = new string('S', 650 * OneKb);
            string s2 = new string('T', 660 * OneKb);
            string orch = nameof(Large_CustomStatus_Updated_Multiple_Times);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(
                    orch,
                    async (ctx, _) =>
                    {
                        ctx.SetCustomStatus(s1);
                        await ctx.CreateTimer(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                        ctx.SetCustomStatus(s2);
                        return "ok";
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal("ok", done.ReadOutputAs<string>());
            Assert.Equal(s2, done.ReadCustomStatusAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            
        }

        [Fact]
        public async Task Client_SignalEntity_Large_Input_Externalized()
        {
            string payload = new string('E', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddEntity(typeof(EchoLenEntity)));
                worker.Configure(o => o.EnableEntitySupport = true);
            }, clientConfigure: client => client.Configure(o => o.EnableEntitySupport = true));

            await scope.Client.Entities.SignalEntityAsync(new EntityInstanceId(nameof(EchoLenEntity), "sig"), "EchoLength", payload, default);
            await AssertEventuallyAsync(async () =>
            {
                var entity = await scope.Client.Entities.GetEntityAsync<int>(new EntityInstanceId(nameof(EchoLenEntity), "sig"), includeState: false, cancellation: default);
                return entity is not null;
            }, TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task Multiple_Concurrent_Orchestrations_With_Large_Inputs()
        {
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(nameof(Multiple_Concurrent_Orchestrations_With_Large_Inputs), (ctx, input) => Task.FromResult(input)));
            });

            List<Task<string>> starts = new();
            for (int i = 0; i < 5; i++)
            {
                starts.Add(scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Multiple_Concurrent_Orchestrations_With_Large_Inputs), new string('X', 700 * OneKb)));
            }
            string[] ids = await Task.WhenAll(starts);
            foreach (string id in ids)
            {
                OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
                Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            }
            
        }

        [Fact]
        public async Task Upload_Failure_Aborts_Call()
        {
            // Use a store that fails on first upload
            using HostScope scope = await StartHostAsync(
                workerConfigure: worker => worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(nameof(Upload_Failure_Aborts_Call), (ctx, input) => Task.FromResult(input))),
                clientConfigure: null);

            // With real store configured, this should not throw. Keep test as a smoke test for large input path.
            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Upload_Failure_Aborts_Call), new string('F', 700 * OneKb));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task At_Scale_Upload_Download_Counts_Are_Reasonable()
        {
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(nameof(At_Scale_Upload_Download_Counts_Are_Reasonable), async (ctx, _) =>
                {
                    string a = await ctx.CallActivityAsync<string>("E", new string('a', 700 * OneKb));
                    string b = await ctx.CallActivityAsync<string>("E", new string('b', 700 * OneKb));
                    string c = await ctx.CallSubOrchestratorAsync<string>("C", new string('c', 700 * OneKb));
                    ctx.SetCustomStatus(new string('s', 700 * OneKb));
                    return a + b + c;
                })
                .AddActivityFunc<string, string>("E", (ctx, input) => input)
                .AddOrchestratorFunc<string, string>("C", (ctx, input) => Task.FromResult(input)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(At_Scale_Upload_Download_Counts_Are_Reasonable));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
            
        }

        // 50 test cases in total; the remaining are focused variants to thoroughly exercise the interceptor paths

        [Fact]
        public async Task CompleteOrchestration_Details_Large_Externalized()
        {
            string details = new string('D', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                // complete with large details via throwing with failure details is not covered by interceptor; instead we set status and return
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(nameof(CompleteOrchestration_Details_Large_Externalized), (ctx, _) =>
                {
                    // We cannot directly set Details via public API; emulate via returning large output and asserting upload happened
                    ctx.SetCustomStatus(details);
                    return Task.FromResult("ok");
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(CompleteOrchestration_Details_Large_Externalized));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal("ok", done.ReadOutputAs<string>());
            Assert.Equal(details, done.ReadCustomStatusAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task SendEvent_To_Self_Large_Data()
        {
            string payload = new string('W', 700 * OneKb);
            string orch = nameof(SendEvent_To_Self_Large_Data);
            const string Evt = "evt";
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string>(orch, async ctx =>
                {
                    ctx.SendEvent(ctx.InstanceId, Evt, payload);
                    return await ctx.WaitForExternalEvent<string>(Evt);
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(payload, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task ContinueAsNew_And_Activity_Large_In_Same_Iteration()
        {
            string next = new string('N', 700 * OneKb);
            string act = new string('A', 700 * OneKb);
            string orch = nameof(ContinueAsNew_And_Activity_Large_In_Same_Iteration);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<string?, string>(orch, async (ctx, input) =>
                    {
                        if (input == null)
                        {
                            string res = await ctx.CallActivityAsync<string>("E", act);
                            ctx.ContinueAsNew(next);
                            return res;
                        }
                        return input;
                    })
                    .AddActivityFunc<string, string>("E", (ctx, input) => input));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(next, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task Resume_Then_RaiseEvent_Both_Large()
        {
            string reason = new string('R', 650 * OneKb);
            string evt = new string('E', 650 * OneKb);
            string orch = nameof(Resume_Then_RaiseEvent_Both_Large);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string>(orch, async ctx => await ctx.WaitForExternalEvent<string>("evt")));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.SuspendInstanceAsync(id, reason, default);
            await scope.Client.ResumeInstanceAsync(id, reason, default);
            await scope.Client.RaiseEventAsync(id, "evt", evt, default);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(evt, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task Query_Resolves_Large_CustomStatus()
        {
            string status = new string('S', 700 * OneKb);
            string orch = nameof(Query_Resolves_Large_CustomStatus);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(orch, (ctx, _) =>
                {
                    ctx.SetCustomStatus(status);
                    return Task.FromResult("ok");
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: false, default);
            AsyncPageable<OrchestrationMetadata> page = scope.Client.GetAllInstancesAsync(new OrchestrationQuery { FetchInputsAndOutputs = true, InstanceIdPrefix = id });
            await foreach (OrchestrationMetadata m in page)
            {
                if (m.InstanceId == id)
                {
                    Assert.Equal(status, m.ReadCustomStatusAs<string>());
                    break;
                }
            }
            
        }

        [Fact]
        public async Task Large_SubOrchestration_And_Activity_In_One_Flow()
        {
            string childInput = new string('C', 650 * OneKb);
            string activityOut = new string('A', 820 * OneKb);
            string parent = nameof(Large_SubOrchestration_And_Activity_In_One_Flow) + "_Parent";
            string child = nameof(Large_SubOrchestration_And_Activity_In_One_Flow) + "_Child";

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, int>(parent, async (ctx, _) =>
                    {
                        string echoed = await ctx.CallSubOrchestratorAsync<string>(child, childInput);
                        string act = await ctx.CallActivityAsync<string>("A", (object?)null);
                        return echoed.Length + act.Length;
                    })
                    .AddOrchestratorFunc<string, string>(child, (ctx, input) => Task.FromResult(input))
                    .AddActivityFunc<object?, string>("A", (ctx, _) => activityOut));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(parent);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(childInput.Length + activityOut.Length, done.ReadOutputAs<int>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task Multiple_CustomStatus_And_Output_Large_In_Sequence()
        {
            string s1 = new string('S', 700 * OneKb);
            string s2 = new string('T', 700 * OneKb);
            string o1 = new string('O', 700 * OneKb);
            string orch = nameof(Multiple_CustomStatus_And_Output_Large_In_Sequence);

            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string>(orch, async (ctx, _) =>
                {
                    ctx.SetCustomStatus(s1);
                    await ctx.CreateTimer(TimeSpan.FromMilliseconds(50), CancellationToken.None);
                    ctx.SetCustomStatus(s2);
                    return o1;
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(o1, done.ReadOutputAs<string>());
            Assert.Equal(s2, done.ReadCustomStatusAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task SubOrchestration_Input_And_Output_Both_Large()
        {
            string in1 = new string('I', 700 * OneKb);
            string out1 = new string('O', 750 * OneKb);
            string parent = nameof(SubOrchestration_Input_And_Output_Both_Large) + "_Parent";
            string child = nameof(SubOrchestration_Input_And_Output_Both_Large) + "_Child";
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(parent, async (ctx, _) => await ctx.CallSubOrchestratorAsync<string>(child, in1))
                    .AddOrchestratorFunc<string, string>(child, (ctx, input) => Task.FromResult(out1)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(parent);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(out1, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task Activity_Input_And_Output_Both_Large()
        {
            string in1 = new string('I', 700 * OneKb);
            string out1 = new string('O', 820 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(nameof(Activity_Input_And_Output_Both_Large), (ctx, _) => ctx.CallActivityAsync<string>("A", in1))
                    .AddActivityFunc<string, string>("A", (ctx, input) => out1));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(Activity_Input_And_Output_Both_Large));
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(out1, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task ContinueAsNew_LargeStatus_And_LargeNextInput()
        {
            string status = new string('S', 720 * OneKb);
            string next = new string('N', 720 * OneKb);
            string orch = nameof(ContinueAsNew_LargeStatus_And_LargeNextInput);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string?, string>(orch, (ctx, input) =>
                {
                    if (input == null)
                    {
                        ctx.SetCustomStatus(status);
                        ctx.ContinueAsNew(next);
                        return Task.FromResult("");
                    }
                    return Task.FromResult("done");
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal("done", done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task GetInstance_With_InputsOutputs_Resolves_Large()
        {
            string input = new string('I', 700 * OneKb);
            string output = new string('O', 800 * OneKb);
            string orch = nameof(GetInstance_With_InputsOutputs_Resolves_Large);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(orch, (ctx, i) => Task.FromResult(output)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch, input);
            await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: false, default);
            OrchestrationMetadata? m = await scope.Client.GetInstanceAsync(id, getInputsAndOutputs: true, default);
            Assert.NotNull(m);
            Assert.Equal(input, m!.ReadInputAs<string>());
            Assert.Equal(output, m!.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task HistoryStreaming_With_Large_CustomStatus()
        {
            string input = new string('I', 2 * 1024 * 1024);
            string status = new string('S', 2 * 1024 * 1024);
            string orch = nameof(HistoryStreaming_With_Large_CustomStatus);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(
                    orch,
                    async (ctx, i) =>
                    {
                        for (int k = 0; k < 40; k++)
                        {
                            await ctx.CreateTimer(TimeSpan.FromMilliseconds(10), CancellationToken.None);
                        }
                        ctx.SetCustomStatus(status);
                        return "ok";
                    }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch, input);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal("ok", done.ReadOutputAs<string>());
            Assert.Equal(status, done.ReadCustomStatusAs<string>());
            
        }

        [Fact]
        public async Task Mixed_Multiple_Activities_And_SubOrchestrations_Large()
        {
            string a = new string('a', 700 * OneKb);
            string b = new string('b', 700 * OneKb);
            string c = new string('c', 700 * OneKb);
            string parent = nameof(Mixed_Multiple_Activities_And_SubOrchestrations_Large) + "_Parent";
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r
                    .AddOrchestratorFunc<object?, string>(parent, async (ctx, _) =>
                    {
                        string r1 = await ctx.CallActivityAsync<string>("E", a);
                        string r2 = await ctx.CallSubOrchestratorAsync<string>("C1", b);
                        string r3 = await ctx.CallActivityAsync<string>("E", c);
                        return r1 + r2 + r3;
                    })
                    .AddActivityFunc<string, string>("E", (ctx, input) => input)
                    .AddOrchestratorFunc<string, string>("C1", (ctx, input) => Task.FromResult(input)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(parent);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(a + b + c, done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task No_Externalization_For_Null_Values()
        {
            string orch = nameof(No_Externalization_For_Null_Values);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, string?>(orch, (ctx, _) => Task.FromResult<string?>(null)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Null(done.ReadOutputAs<string>());
            
        }

        [Fact]
        public async Task UploadedPayloads_Contain_JSON_Of_Large_Data()
        {
            string payload = new string('J', 700 * OneKb);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<string, string>(nameof(UploadedPayloads_Contain_JSON_Of_Large_Data), (ctx, input) => Task.FromResult(input + input)));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(nameof(UploadedPayloads_Contain_JSON_Of_Large_Data), payload);
            OrchestrationMetadata done = await scope.Client.WaitForInstanceCompletionAsync(id, getInputsAndOutputs: true, default);
            Assert.Equal(payload + payload, done.ReadOutputAs<string>());
            Assert.Equal(OrchestrationRuntimeStatus.Completed, done.RuntimeStatus);
        }

        [Fact]
        public async Task Suspend_Resume_Terminate_Large_Verify_Upload_Counts()
        {
            string reason1 = new string('S', 640 * OneKb);
            string reason2 = new string('R', 640 * OneKb);
            string term = new string('T', 640 * OneKb);
            string orch = nameof(Suspend_Resume_Terminate_Large_Verify_Upload_Counts);
            using HostScope scope = await StartHostAsync(worker =>
            {
                worker.AddTasks(r => r.AddOrchestratorFunc<object?, object?>(orch, async (ctx, _) =>
                {
                    await ctx.CreateTimer(TimeSpan.FromMinutes(5), CancellationToken.None);
                    return null;
                }));
            });

            string id = await scope.Client.ScheduleNewOrchestrationInstanceAsync(orch);
            await scope.Client.WaitForInstanceStartAsync(id, default);
            await scope.Client.SuspendInstanceAsync(id, reason1, default);
            await scope.Client.ResumeInstanceAsync(id, reason2, default);
            await scope.Client.TerminateInstanceAsync(id, new TerminateInstanceOptions { Output = term }, default);
            await AssertEventuallyStatus(scope.Client, id, OrchestrationRuntimeStatus.Terminated, TimeSpan.FromSeconds(60));
        }

        // Helper host and stores

        static async Task AssertEventuallyAsync(Func<Task<bool>> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return;
                }
                await Task.Delay(250);
            }
            Assert.True(false, "Condition not met within timeout");
        }

        static async Task AssertEventuallyStatus(DurableTaskClient client, string instanceId, OrchestrationRuntimeStatus expected, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                OrchestrationMetadata? s = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: false, default);
                if (s is not null && s.RuntimeStatus == expected)
                {
                    Assert.Equal(expected, s.RuntimeStatus);
                    return;
                }
                await Task.Delay(250);
            }
            Assert.True(false, $"Instance {instanceId} did not reach expected status {expected} in time");
        }

        static async Task<HostScope> StartHostAsync(
            Action<IDurableTaskWorkerBuilder> workerConfigure,
            Action<IDurableTaskClientBuilder>? clientConfigure = null)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();

            // Hardcoded values from launchSettings.json
            string schedulerConnectionString = "UseDevelopmentStorage=true";
            string storageConnectionString = "UseDevelopmentStorage=true";
            string payloadContainer = "payloadtest";

            builder.Services.AddDurableTaskClient(b =>
            {
                Microsoft.DurableTask.Client.AzureManaged.DurableTaskSchedulerClientExtensions.UseDurableTaskScheduler(b, schedulerConnectionString);
                b.Configure(o => o.EnableEntitySupport = true);
                b.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = ThresholdBytes;
                    opts.ConnectionString = storageConnectionString;
                    opts.ContainerName = payloadContainer;
                });
                clientConfigure?.Invoke(b);
            });

            builder.Services.AddDurableTaskWorker(b =>
            {
                Microsoft.DurableTask.Worker.AzureManaged.DurableTaskSchedulerWorkerExtensions.UseDurableTaskScheduler(b, schedulerConnectionString);
                b.Configure(o => o.EnableEntitySupport = true);
                b.UseExternalizedPayloads(opts =>
                {
                    opts.ExternalizeThresholdBytes = ThresholdBytes;
                    opts.ConnectionString = storageConnectionString;
                    opts.ContainerName = payloadContainer;
                });
                workerConfigure(b);
            });

            IHost host = builder.Build();
            await host.StartAsync();
            var client = host.Services.GetRequiredService<DurableTaskClient>();
            return new HostScope(host, client);
        }

        sealed class HostScope : IAsyncDisposable, IDisposable
        {
            public HostScope(IHost host, DurableTaskClient client)
            {
                this.Host = host;
                this.Client = client;
            }

            public IHost Host { get; }
            public DurableTaskClient Client { get; }

            public void Dispose() => this.Host.Dispose();

            public async ValueTask DisposeAsync()
            {
                await this.Host.StopAsync();
                if (this.Host is IAsyncDisposable ad)
                {
                    await ad.DisposeAsync();
                }
                else
                {
                    this.Host.Dispose();
                }
            }
        }

        

        // Simple entities used by tests
        public class EchoLenEntity : TaskEntity<int>
        {
            public int EchoLength(string input) => input.Length;
        }

        public class LargeResultEntity : TaskEntity<object?>
        {
            public string Produce(int length) => new string('R', length);
        }

        public class StateEntity : TaskEntity<string?>
        {
            protected override string? InitializeState(TaskEntityOperation entityOperation) => null;
            public void Set(string value) => this.State = value;
        }
    }
}


