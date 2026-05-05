// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskActivityShimMiddlewareTests
{
    [Fact]
    public async Task RunAsync_WithMiddleware_RunsInRegistrationOrderAndPopulatesContext()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddScoped<CallLog>();
        services.AddScoped<ScopedMarker>();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseActivityMiddleware<FirstActivityMiddleware>();
        builder.UseActivityMiddleware(async (context, next) =>
        {
            CallLog log = context.Features.Get<CallLog>()
                ?? throw new InvalidOperationException("The call log feature was not available.");
            log.Entries.Add("delegate-before");
            context.Result.Should().BeNull();

            await next(context);

            context.Result.Should().Be("output");
            log.Entries.Add("delegate-after");
        });
        builder.UseActivityMiddleware<SecondActivityMiddleware>();

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
        TaskActivity shim = factory.CreateActivity(
            "TestActivity",
            FuncTaskActivity.Create<string, string>((context, input) =>
            {
                callLog.Entries.Add("body");
                callLog.ActivityContextFromBody = context;
                return Task.FromResult("output");
            }),
            scope.ServiceProvider,
            features);
        TaskContext innerContext = new(new OrchestrationInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            ExecutionId = Guid.NewGuid().ToString(),
        });

        // Act
        string? output = await shim.RunAsync(innerContext, "[\"input\"]");

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
        callLog.ActivityMiddlewareContext.Should().NotBeNull();
        TaskActivityMiddlewareContext middlewareContext = callLog.ActivityMiddlewareContext!;
        middlewareContext.Name.Should().Be(new TaskName("TestActivity"));
        middlewareContext.InstanceId.Should().Be(innerContext.OrchestrationInstance.InstanceId);
        middlewareContext.InputType.Should().Be(typeof(string));
        middlewareContext.Input.Should().Be("input");
        middlewareContext.RawInput.Should().Be("\"input\"");
        middlewareContext.ActivityContext.Should().BeSameAs(callLog.ActivityContextFromBody);
        middlewareContext.Features.Should().BeSameAs(features);
        middlewareContext.Services.Should().BeSameAs(scope.ServiceProvider);
        middlewareContext.CancellationToken.Should().Be(CancellationToken.None);
        middlewareContext.Result.Should().Be("output");
    }

    [Fact]
    public async Task RunAsync_WhenMiddlewareSetsResult_SkipsActivityBody()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseActivityMiddleware((context, next) =>
        {
            context.SetResult("short-circuited");
            return Task.CompletedTask;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        bool bodyCalled = false;
        TaskActivity shim = factory.CreateActivity(
            "TestActivity",
            FuncTaskActivity.Create<string, string>((context, input) =>
            {
                bodyCalled = true;
                return Task.FromResult("output");
            }));
        TaskContext innerContext = new(new OrchestrationInstance { InstanceId = "instance" });

        // Act
        string? output = await shim.RunAsync(innerContext, "[\"input\"]");

        // Assert
        output.Should().Be("\"short-circuited\"");
        bodyCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RunActivityAsync_WithMiddleware_ReturnsRawResult()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        object expected = new { Message = "from-body" };
        builder.UseActivityMiddleware(async (context, next) =>
        {
            await next(context);
            context.Result.Should().BeSameAs(expected);
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        TaskContext innerContext = new(new OrchestrationInstance { InstanceId = "instance" });

        // Act
        object? result = await factory.RunActivityAsync(
            "TestActivity",
            FuncTaskActivity.Create<string, object?>((context, input) => Task.FromResult<object?>(expected)),
            innerContext,
            "\"input\"");

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void DurableTaskShimFactory_WithActivityMiddleware_HasActivityMiddlewareIsTrue()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseActivityMiddleware((context, next) => next(context));
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);

        // Assert
        factory.HasActivityMiddleware.Should().BeTrue();
    }

    [Fact]
    public void DurableTaskShimFactory_WithoutActivityMiddleware_HasActivityMiddlewareIsFalse()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);

        // Assert
        factory.HasActivityMiddleware.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_TypeMiddleware_ResolvesFromInvocationScope()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddScoped<ScopedMarker>();
        services.AddSingleton<ScopeCapture>();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseActivityMiddleware<ScopedActivityMiddleware>();

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        ScopeCapture capture = provider.GetRequiredService<ScopeCapture>();

        // Act
        for (int i = 0; i < 2; i++)
        {
            using IServiceScope scope = provider.CreateScope();
            TaskActivity shim = factory.CreateActivity(
                "TestActivity",
                FuncTaskActivity.Create<string, string>((context, input) => Task.FromResult("output")),
                scope.ServiceProvider);
            await shim.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = $"instance-{i}" }), "[\"input\"]");
        }

        // Assert
        capture.MarkerIds.Should().HaveCount(2);
        capture.MarkerIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RunAsync_DelegateMiddleware_RunsWithoutTypeResolution()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        bool middlewareCalled = false;
        builder.UseActivityMiddleware(async (context, next) =>
        {
            middlewareCalled = true;
            await next(context);
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        TaskActivity shim = factory.CreateActivity(
            "TestActivity",
            FuncTaskActivity.Create<string, string>((context, input) => Task.FromResult("output")));

        // Act
        string? output = await shim.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "instance" }),
            "[\"input\"]");

        // Assert
        output.Should().Be("\"output\"");
        middlewareCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenMiddlewareThrows_PropagatesException()
    {
        // Arrange
        InvalidOperationException expected = new("middleware failed");
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.UseActivityMiddleware((context, next) => throw expected);

        using ServiceProvider provider = services.BuildServiceProvider();
        DurableTaskShimFactory factory = new(
            workerName: "test",
            services: provider,
            options: null,
            loggerFactory: NullLoggerFactory.Instance);
        TaskActivity shim = factory.CreateActivity(
            "TestActivity",
            FuncTaskActivity.Create<string, string>((context, input) => Task.FromResult("output")));

        // Act
        Func<Task> act = async () => await shim.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "instance" }),
            "[\"input\"]");

        // Assert
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RunAsync_WithNoMiddleware_PreservesExistingBehavior()
    {
        // Arrange
        TaskActivity shim = DurableTaskShimFactory.Default.CreateActivity(
            "TestActivity",
            FuncTaskActivity.Create<string, string>((context, input) => Task.FromResult($"{input}-body")));

        // Act
        string? output = await shim.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "instance" }),
            "[\"input\"]");

        // Assert
        output.Should().Be("\"input-body\"");
    }

    sealed class FirstActivityMiddleware : ITaskActivityMiddleware
    {
        readonly CallLog log;
        readonly ScopedMarker marker;

        public FirstActivityMiddleware(CallLog log, ScopedMarker marker)
        {
            this.log = log;
            this.marker = marker;
        }

        public async Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
        {
            this.log.MarkerIds.Add(this.marker.Id);
            this.log.ActivityMiddlewareContext = context;
            this.log.Entries.Add("first-before");
            context.GetInput<string>().Should().Be("input");
            context.Result.Should().BeNull();

            await next(context);

            context.Result.Should().Be("output");
            this.log.Entries.Add("first-after");
        }
    }

    sealed class SecondActivityMiddleware : ITaskActivityMiddleware
    {
        readonly CallLog log;

        public SecondActivityMiddleware(CallLog log)
        {
            this.log = log;
        }

        public async Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
        {
            this.log.Entries.Add("second-before");

            await next(context);

            context.Result.Should().Be("output");
            this.log.Entries.Add("second-after");
        }
    }

    sealed class ScopedActivityMiddleware : ITaskActivityMiddleware
    {
        readonly ScopedMarker marker;
        readonly ScopeCapture capture;

        public ScopedActivityMiddleware(ScopedMarker marker, ScopeCapture capture)
        {
            this.marker = marker;
            this.capture = capture;
        }

        public async Task InvokeAsync(TaskActivityMiddlewareContext context, TaskActivityMiddlewareDelegate next)
        {
            this.capture.MarkerIds.Add(this.marker.Id);

            await next(context);
        }
    }

    sealed class CallLog
    {
        public List<string> Entries { get; } = [];

        public List<Guid> MarkerIds { get; } = [];

        public TaskActivityMiddlewareContext? ActivityMiddlewareContext { get; set; }

        public TaskActivityContext? ActivityContextFromBody { get; set; }
    }

    sealed class ScopedMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    sealed class ScopeCapture
    {
        public List<Guid> MarkerIds { get; } = [];
    }
}
