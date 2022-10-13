// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.Tests;

public class DurableTaskBuilderExtensionsTests
{
    [Fact]
    public void UseBuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget(typeof(BadBuildTarget));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void UseBuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget(typeof(GoodBuildTarget));
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void UseBuildTargetT_ValidType_Sets()
    {
        DefaultDurableTaskBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget<GoodBuildTarget>();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void AddTasks_ConfiguresRegistry()
    {
        ServiceCollection services = new();
        DefaultDurableTaskBuilder builder = new("test", services);

        DurableTaskRegistry? actual = null;
        builder.AddTasks(registry => actual = registry);
        DurableTaskRegistry expected = services.BuildServiceProvider().GetOptions<DurableTaskRegistry>("test");

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void Configure_ConfiguresOptions()
    {
        ServiceCollection services = new();
        DefaultDurableTaskBuilder builder = new("test", services);

        DurableTaskWorkerOptions? actual = null;
        builder.Configure(options => actual = options);
        DurableTaskWorkerOptions expected = services.BuildServiceProvider().GetOptions<DurableTaskWorkerOptions>("test");

        actual.Should().BeSameAs(expected);
    }

    class BadBuildTarget : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }

    class GoodBuildTarget : DurableTaskWorker
    {
        public GoodBuildTarget(string name, DurableTaskFactory factory, DurableTaskWorkerOptions options)
            : base(name, factory, options)
        {
        }

        public new string Name => base.Name;

        public new DurableTaskFactory Factory => base.Factory;

        public new DurableTaskWorkerOptions Options => base.Options;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }
}
