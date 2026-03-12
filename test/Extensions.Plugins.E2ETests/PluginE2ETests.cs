// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Extensions.Plugins.E2ETests;

/// <summary>
/// End-to-end tests for the plugin system against a real Durable Task Scheduler.
/// Requires the DTS_CONNECTION_STRING environment variable to be set.
/// </summary>
[Collection("DTS")]
[Trait("Category", "E2E")]
public class PluginE2ETests : IAsyncLifetime
{
    readonly ITestOutputHelper output;
    readonly DtsFixture fixture = new();
    readonly string testId = Guid.NewGuid().ToString("N")[..8];

    public PluginE2ETests(ITestOutputHelper output)
    {
        this.output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await this.fixture.DisposeAsync();

    /// <summary>
    /// Returns true if DTS_CONNECTION_STRING is set; tests should return early if false.
    /// </summary>
    bool HasConnectionString()
    {
        string? cs = DtsFixture.GetConnectionString();
        if (string.IsNullOrEmpty(cs))
        {
            this.output.WriteLine("SKIPPED: DTS_CONNECTION_STRING not set.");
            return false;
        }

        return true;
    }

    // ─── Test 1: Metrics plugin tracks orchestration and activity counts ───

    [Fact]
    public async Task MetricsPlugin_TracksExecutionCounts_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"MetricsOrch_{this.testId}";
        string actName = $"MetricsAct_{this.testId}";
        MetricsStore store = new();

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    string r = await ctx.CallActivityAsync<string>(actName, "hello");
                    return r;
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => $"echo:{input}");
            },
            worker => worker.UseMetricsPlugin(store));

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);
        this.output.WriteLine($"Scheduled: {instanceId}");

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        this.output.WriteLine($"Output: {result.SerializedOutput}");

        // Metrics plugin interceptors should have been invoked via the wrapper.
        store.GetOrchestrationMetrics(orchName).Started.Should().BeGreaterOrEqualTo(1);
        store.GetOrchestrationMetrics(orchName).Completed.Should().BeGreaterOrEqualTo(1);
        store.GetActivityMetrics(actName).Started.Should().BeGreaterOrEqualTo(1);
        store.GetActivityMetrics(actName).Completed.Should().BeGreaterOrEqualTo(1);
        store.GetOrchestrationMetrics(orchName).Failed.Should().Be(0);
        store.GetActivityMetrics(actName).Failed.Should().Be(0);

        this.output.WriteLine($"Orch started={store.GetOrchestrationMetrics(orchName).Started}, completed={store.GetOrchestrationMetrics(orchName).Completed}");
        this.output.WriteLine($"Activity started={store.GetActivityMetrics(actName).Started}, completed={store.GetActivityMetrics(actName).Completed}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 2: Logging plugin fires interceptors without errors ───

    [Fact]
    public async Task LoggingPlugin_DoesNotBreakExecution_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"LogOrch_{this.testId}";
        string actName = $"LogAct_{this.testId}";

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    return await ctx.CallActivityAsync<string>(actName, "world");
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => $"Hi {input}");
            },
            worker => worker.UseLoggingPlugin());

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);
        this.output.WriteLine($"Scheduled: {instanceId}");

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.SerializedOutput.Should().Contain("Hi world");
        this.output.WriteLine($"Completed with output: {result.SerializedOutput}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 3: Authorization plugin blocks unauthorized execution ───

    [Fact]
    public async Task AuthorizationPlugin_BlocksUnauthorized_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"AuthBlockOrch_{this.testId}";
        string actName = $"AuthBlockAct_{this.testId}";

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    return await ctx.CallActivityAsync<string>(actName, "data");
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => input);
            },
            worker => worker.UseAuthorizationPlugin(new DenyAllHandler()));

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);
        this.output.WriteLine($"Scheduled: {instanceId}");

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        // The orchestration should fail because the authorization interceptor denies it.
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Failed);
        result.FailureDetails.Should().NotBeNull();
        result.FailureDetails!.ErrorMessage.Should().Contain("Authorization denied");
        this.output.WriteLine($"Failed as expected: {result.FailureDetails.ErrorMessage}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 4: Authorization plugin allows authorized execution ───

    [Fact]
    public async Task AuthorizationPlugin_AllowsAuthorized_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"AuthAllowOrch_{this.testId}";
        string actName = $"AuthAllowAct_{this.testId}";

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    return await ctx.CallActivityAsync<string>(actName, "allowed");
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => $"ok:{input}");
            },
            worker => worker.UseAuthorizationPlugin(new AllowAllHandler()));

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.SerializedOutput.Should().Contain("ok:allowed");
        this.output.WriteLine($"Authorized OK: {result.SerializedOutput}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 5: Validation plugin rejects invalid input ───

    [Fact]
    public async Task ValidationPlugin_RejectsInvalidInput_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"ValRejectOrch_{this.testId}";
        string actName = $"ValRejectAct_{this.testId}";

        // Validator rejects null/empty inputs for the activity.
        IInputValidator validator = new NonEmptyStringValidator(actName);

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    // Pass empty string — should be rejected by validation.
                    return await ctx.CallActivityAsync<string>(actName, string.Empty);
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => input);
            },
            worker => worker.UseValidationPlugin(validator));

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        // Orchestration completes but the activity call should fail with TaskFailedException
        // because the validation interceptor throws ArgumentException.
        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Failed);
        this.output.WriteLine($"Validation rejected: {result.FailureDetails?.ErrorMessage}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 6: Validation plugin passes valid input ───

    [Fact]
    public async Task ValidationPlugin_AcceptsValidInput_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"ValAcceptOrch_{this.testId}";
        string actName = $"ValAcceptAct_{this.testId}";

        IInputValidator validator = new NonEmptyStringValidator(actName);

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    return await ctx.CallActivityAsync<string>(actName, "valid-data");
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => $"processed:{input}");
            },
            worker => worker.UseValidationPlugin(validator));

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.SerializedOutput.Should().Contain("processed:valid-data");
        this.output.WriteLine($"Valid OK: {result.SerializedOutput}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 7: Plugin-provided activities are callable ───

    [Fact]
    public async Task PluginRegisteredActivity_IsCallable_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"PluginTaskOrch_{this.testId}";
        string pluginActName = $"PluginAct_{this.testId}";

        SimplePlugin taskPlugin = SimplePlugin.NewBuilder("E2E.TaskPlugin")
            .AddTasks(registry =>
            {
                registry.AddActivityFunc<string, string>(pluginActName, (ctx, input) => $"plugin:{input}");
            })
            .Build();

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    // Call the activity that was registered by the plugin, not by the user.
                    return await ctx.CallActivityAsync<string>(pluginActName, "from-orch");
                });
            },
            worker => worker.UsePlugin(taskPlugin));

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.SerializedOutput.Should().Contain("plugin:from-orch");
        this.output.WriteLine($"Plugin activity result: {result.SerializedOutput}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 8: Multiple plugins all fire interceptors ───

    [Fact]
    public async Task MultiplePlugins_AllInterceptorsFire_E2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"MultiPlugOrch_{this.testId}";
        string actName = $"MultiPlugAct_{this.testId}";
        MetricsStore store = new();

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    return await ctx.CallActivityAsync<string>(actName, "multi");
                });
                tasks.AddActivityFunc<string, string>(actName, (ctx, input) => $"done:{input}");
            },
            worker =>
            {
                worker.UseLoggingPlugin();
                worker.UseMetricsPlugin(store);
                worker.UseAuthorizationPlugin(new AllowAllHandler());
            });

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.SerializedOutput.Should().Contain("done:multi");

        // Metrics plugin should have recorded counts because all plugins fire.
        store.GetOrchestrationMetrics(orchName).Started.Should().BeGreaterOrEqualTo(1);
        store.GetActivityMetrics(actName).Started.Should().BeGreaterOrEqualTo(1);
        this.output.WriteLine($"Multiple plugins OK. Orch started={store.GetOrchestrationMetrics(orchName).Started}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Test 9: Plugin with both tasks and interceptors works E2E ───

    [Fact]
    public async Task PluginWithTasksAndInterceptors_WorksE2E()
    {
        if (!this.HasConnectionString()) return;
        string orchName = $"FullPlugOrch_{this.testId}";
        string pluginActName = $"FullPlugAct_{this.testId}";
        MetricsStore store = new();

        SimplePlugin fullPlugin = SimplePlugin.NewBuilder("E2E.FullPlugin")
            .AddTasks(registry =>
            {
                registry.AddActivityFunc<string, string>(pluginActName, (ctx, input) => $"full:{input}");
            })
            .Build();

        await this.fixture.StartAsync(
            tasks =>
            {
                tasks.AddOrchestratorFunc(orchName, async ctx =>
                {
                    return await ctx.CallActivityAsync<string>(pluginActName, "test");
                });
            },
            worker =>
            {
                worker.UsePlugin(fullPlugin);
                worker.UseMetricsPlugin(store);
            });

        string instanceId = await this.fixture.Client.ScheduleNewOrchestrationInstanceAsync(orchName);

        OrchestrationMetadata result = await this.fixture.Client.WaitForInstanceCompletionAsync(
            instanceId, getInputsAndOutputs: true, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        result.RuntimeStatus.Should().Be(OrchestrationRuntimeStatus.Completed);
        result.SerializedOutput.Should().Contain("full:test");

        // Metrics interceptors wrapped the plugin-provided activity too.
        store.GetActivityMetrics(pluginActName).Started.Should().BeGreaterOrEqualTo(1);
        this.output.WriteLine($"Full plugin OK. Activity started={store.GetActivityMetrics(pluginActName).Started}");

        await this.fixture.Client.PurgeInstanceAsync(instanceId);
    }

    // ─── Helper classes ───

    sealed class DenyAllHandler : IAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(AuthorizationContext context) => Task.FromResult(false);
    }

    sealed class AllowAllHandler : IAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(AuthorizationContext context) => Task.FromResult(true);
    }

    sealed class NonEmptyStringValidator : IInputValidator
    {
        readonly string targetActivity;

        public NonEmptyStringValidator(string targetActivity) => this.targetActivity = targetActivity;

        public Task<ValidationResult> ValidateAsync(TaskName taskName, object? input)
        {
            if (taskName.Name == this.targetActivity && input is string s && string.IsNullOrEmpty(s))
            {
                return Task.FromResult(ValidationResult.Failure("Input must be non-empty."));
            }

            return Task.FromResult(ValidationResult.Success);
        }
    }
}


