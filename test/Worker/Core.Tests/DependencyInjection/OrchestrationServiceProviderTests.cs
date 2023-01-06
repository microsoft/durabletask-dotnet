// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Worker.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.DependencyInjection.Tests;

public sealed class OrchestrationServiceProviderTests : IDisposable
{
    readonly IServiceProvider serviceProvider;

    public OrchestrationServiceProviderTests()
    {
        // Ensure not set before test.
        TaskOrchestrationContextAccessor.Current = null;
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(new MyService());
        this.serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        // Ensure not set after test.
        TaskOrchestrationContextAccessor.Current = null;
    }

    [Fact]
    public void Ctor_Null_Throws()
    {
        Func<OrchestrationServiceProvider> act = () => new OrchestrationServiceProvider(null!);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("innerProvider");
    }

    [Fact]
    public void GetService_Null_Throws()
    {
        OrchestrationServiceProvider services = new(this.serviceProvider);
        Action act = () => services.GetService(null!);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("serviceType");
    }

    [Fact]
    public void GetService_UnsupportedType_Throws()
    {
        OrchestrationServiceProvider services = new(this.serviceProvider);
        Action act = () => services.GetService(typeof(MyService));
        act.Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void GetService_LoggerFactory_Success()
    {
        OrchestrationServiceProvider services = new(this.serviceProvider);
        object factory = services.GetService(typeof(ILoggerFactory));
        factory.Should().BeOfType<OrchestrationLoggerFactory>();
    }

    [Fact]
    public void GetService_LoggerT_Success()
    {
        TaskOrchestrationContextAccessor.Current = Mock.Of<TaskOrchestrationContext>();
        OrchestrationServiceProvider services = new(this.serviceProvider);
        object logger = services.GetService(typeof(ILogger<OrchestrationServiceProviderTests>));

        logger.Should().BeOfType<Logger<OrchestrationServiceProviderTests>>();
        VerifyLoggerType(logger);
    }

    [Fact]
    public void Activate_Logger_Success()
    {
        TaskOrchestrationContextAccessor.Current = Mock.Of<TaskOrchestrationContext>();
        OrchestrationServiceProvider services = new(this.serviceProvider);
        TestOrchestrator orchestrator = ActivatorUtilities.CreateInstance<TestOrchestrator>(services);

        orchestrator.Logger.Should().NotBeNull();
        VerifyLoggerType(orchestrator.Logger);
    }

    [Fact]
    public void Activate_Options_Throws()
    {
        TaskOrchestrationContextAccessor.Current = Mock.Of<TaskOrchestrationContext>();
        OrchestrationServiceProvider services = new(this.serviceProvider);
        Action act = () => ActivatorUtilities.CreateInstance<BadOrchestrator>(services);

        act.Should().ThrowExactly<NotSupportedException>();
    }

    static void VerifyLoggerType(object logger)
    {
        // Reflection to verify the inner logger is replay-safe
        ILogger inner = (ILogger)logger.GetType()
            .GetField("_logger", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logger)!;
        inner.GetType().Name.Should().Be("ReplaySafeLogger");
    }

    class TestOrchestrator : TaskOrchestrator<object, object>
    {
        public TestOrchestrator(ILogger<TestOrchestrator> logger)
        {
            this.Logger = logger;
        }

        public ILogger Logger { get; }

        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    class BadOrchestrator : TaskOrchestrator<object, object>
    {
        public BadOrchestrator(MyService service)
        {
        }

        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            throw new NotImplementedException();
        }
    }

    class MyService { }
}
