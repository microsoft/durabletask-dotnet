// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskOrchestrationShimMiddlewareTests
{
    [Fact]
    public async Task ExecuteAsync_WithMiddleware_RunsInRegistrationOrderAndPopulatesContext()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddScoped<CallLog>();
        services.AddScoped<ScopedMarker>();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseOrchestrationMiddleware<FirstOrchestrationMiddleware>();
        builder.UseOrchestrationMiddleware(async (context, next) =>
        {
            CallLog log = context.Features.Get<CallLog>()
                ?? throw new InvalidOperationException("The call log feature was not available.");
            log.Entries.Add("delegate-before");
            context.Result.Should().BeNull();

            await next(context);

            context.Result.Should().Be("output");
            log.Entries.Add("delegate-after");
        });
        builder.UseOrchestrationMiddleware<SecondOrchestrationMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        using IServiceScope scope = provider.CreateScope();
        CallLog callLog = scope.ServiceProvider.GetRequiredService<CallLog>();
        MiddlewareFeatureCollection features = new();
        features.Set(callLog);
        ParentOrchestrationInstance parent = new("Parent", "parent-instance");
        TestOrchestrationContext innerContext = new();
        TaskOrchestration shim = factory.CreateOrchestration(
            "TestOrchestrator",
            FuncTaskOrchestrator.Create<string, string>((context, input) =>
            {
                callLog.Entries.Add("body");
                callLog.OrchestrationContextFromBody = context;
                return Task.FromResult("output");
            }),
            scope.ServiceProvider,
            parent,
            features);

        // Act
        string? output = await shim.Execute(innerContext, "\"input\"");

        // Assert
        output.Should().Be("\"output\"");
        callLog.Entries.Should().Equal(
            "first-before",
            "delegate-before",
            "second-before",
            "body",
            "second-after",
            "delegate-after",
            "first-after");
        callLog.OrchestrationMiddlewareContext.Should().NotBeNull();
        TaskOrchestrationMiddlewareContext middlewareContext = callLog.OrchestrationMiddlewareContext!;
        middlewareContext.Name.Should().Be(new TaskName("TestOrchestrator"));
        middlewareContext.InstanceId.Should().Be(innerContext.OrchestrationInstance.InstanceId);
        middlewareContext.Version.Should().Be(innerContext.Version);
        middlewareContext.Parent.Should().Be(parent);
        middlewareContext.Tags.Should().BeNull();
        middlewareContext.IsReplaying.Should().BeFalse();
        middlewareContext.InputType.Should().Be(typeof(string));
        middlewareContext.Input.Should().Be("input");
        middlewareContext.RawInput.Should().Be("\"input\"");
        middlewareContext.OrchestrationContext.Should().BeSameAs(callLog.OrchestrationContextFromBody);
        middlewareContext.Features.Should().BeSameAs(features);
        middlewareContext.CancellationToken.Should().Be(CancellationToken.None);
        middlewareContext.Result.Should().Be("output");
    }

    [Fact]
    public async Task ExecuteAsync_WhenOrchestrationMiddlewareDoesNotCallNext_Throws()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseOrchestrationMiddleware((context, next) => Task.CompletedTask);

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        bool bodyCalled = false;
        TaskOrchestration shim = factory.CreateOrchestration(
            "TestOrchestrator",
            FuncTaskOrchestrator.Create<string, string>((context, input) =>
            {
                bodyCalled = true;
                return Task.FromResult("output");
            }));

        // Act
        Func<Task> act = async () => await shim.Execute(new TestOrchestrationContext(), "\"input\"");

        // Assert
        await act.Should()
            .ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("*next*");
        bodyCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenMiddlewareThrows_PropagatesException()
    {
        // Arrange
        InvalidOperationException expected = new("middleware failed");
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseOrchestrationMiddleware((context, next) => throw expected);

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        TaskOrchestration shim = factory.CreateOrchestration(
            "TestOrchestrator",
            FuncTaskOrchestrator.Create<string, string>((context, input) => Task.FromResult("output")));

        // Act
        Func<Task> act = async () => await shim.Execute(new TestOrchestrationContext(), "\"input\"");

        // Assert
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoMiddleware_PreservesExistingBehavior()
    {
        // Arrange
        TaskOrchestration shim = DurableTaskShimFactory.Default.CreateOrchestration(
            "TestOrchestrator",
            FuncTaskOrchestrator.Create<string, string>((context, input) => Task.FromResult($"{input}-body")));

        // Act
        string? output = await shim.Execute(new TestOrchestrationContext(), "\"input\"");

        // Assert
        output.Should().Be("\"input-body\"");
    }

    sealed class FirstOrchestrationMiddleware : ITaskOrchestrationMiddleware
    {
        readonly CallLog log;
        readonly ScopedMarker marker;

        public FirstOrchestrationMiddleware(CallLog log, ScopedMarker marker)
        {
            this.log = log;
            this.marker = marker;
        }

        public async Task InvokeAsync(
            TaskOrchestrationMiddlewareContext context,
            TaskOrchestrationMiddlewareDelegate next)
        {
            this.log.MarkerIds.Add(this.marker.Id);
            this.log.OrchestrationMiddlewareContext = context;
            this.log.Entries.Add("first-before");
            context.GetInput<string>().Should().Be("input");
            context.Result.Should().BeNull();

            await next(context);

            context.Result.Should().Be("output");
            this.log.Entries.Add("first-after");
        }
    }

    sealed class SecondOrchestrationMiddleware : ITaskOrchestrationMiddleware
    {
        readonly CallLog log;

        public SecondOrchestrationMiddleware(CallLog log)
        {
            this.log = log;
        }

        public async Task InvokeAsync(
            TaskOrchestrationMiddlewareContext context,
            TaskOrchestrationMiddlewareDelegate next)
        {
            this.log.Entries.Add("second-before");

            await next(context);

            context.Result.Should().Be("output");
            this.log.Entries.Add("second-after");
        }
    }

    sealed class CallLog
    {
        public List<string> Entries { get; } = [];

        public List<Guid> MarkerIds { get; } = [];

        public TaskOrchestrationMiddlewareContext? OrchestrationMiddlewareContext { get; set; }

        public TaskOrchestrationContext? OrchestrationContextFromBody { get; set; }
    }

    sealed class ScopedMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    sealed class TestOrchestrationContext : OrchestrationContext
    {
        public TestOrchestrationContext()
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            };
        }

        public override void ContinueAsNew(object input)
        {
            throw new NotImplementedException();
        }

        public override void ContinueAsNew(string newVersion, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name,
            string version,
            string instanceId,
            object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name,
            string version,
            string instanceId,
            object input,
            IDictionary<string, string> tags)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancelToken)
        {
            throw new NotImplementedException();
        }

        public override Task<TResult> ScheduleTask<TResult>(string name, string version, params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
        {
            throw new NotImplementedException();
        }
    }
}
