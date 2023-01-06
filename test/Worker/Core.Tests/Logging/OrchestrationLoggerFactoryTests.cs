// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Tests.Logging;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Worker.Logging.Tests;

public sealed class OrchestrationLoggerFactoryTests : IDisposable
{
    readonly ILoggerFactory loggerFactory;

    public OrchestrationLoggerFactoryTests(ITestOutputHelper output)
    {
        // Ensure not set before test.
        TaskOrchestrationContextAccessor.Current = null;
        this.loggerFactory = new LoggerFactory();
        this.loggerFactory.AddProvider(new TestLogProvider(output));
    }

    public void Dispose()
    {
        // Ensure not set after test.
        TaskOrchestrationContextAccessor.Current = null;
    }

    [Fact]
    public void Ctor_Null_Throws()
    {
        Func<OrchestrationLoggerFactory> act = () => new OrchestrationLoggerFactory(null!);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("innerFactory");
    }

    [Fact]
    public void AddProvider_Throws()
    {
        OrchestrationLoggerFactory factory = new(this.loggerFactory);
        Action act = () => factory.AddProvider(Mock.Of<ILoggerProvider>());
        act.Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void CreateLogger_NoContext_Throws()
    {
        OrchestrationLoggerFactory factory = new(this.loggerFactory);
        Action act = () => factory.CreateLogger("test");
        act.Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateLogger_Context_Creates(bool isReplaying)
    {
        OrchestrationLoggerFactory factory = new(this.loggerFactory);
        Mock<TaskOrchestrationContext> context = new();
        context.Setup(x => x.IsReplaying).Returns(isReplaying);
        TaskOrchestrationContextAccessor.Current = context.Object;

        ILogger logger = factory.CreateLogger("test");
        logger.Should().NotBeNull();
        logger.IsEnabled(LogLevel.Debug).Should().Be(!isReplaying);
        logger.GetType().Name.Should().Be("ReplaySafeLogger");
    }
}
