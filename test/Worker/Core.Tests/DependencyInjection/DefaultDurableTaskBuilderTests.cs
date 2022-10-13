// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.Tests;

public class DefaultDurableTaskBuilderTests
{
    [Fact]
    public void BuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(BadBuildTarget);
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void BuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(GoodBuildTarget);
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));

        builder.BuildTarget = null;
        builder.BuildTarget.Should().BeNull();
    }

    [Fact]
    public void Build_NoTarget_Throws()
    {
        ServiceCollection services = new();
        DefaultDurableTaskBuilder builder = new("test", services);
        Action act = () => builder.Build(services.BuildServiceProvider());
        act.Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void Build_Target_Built()
    {
        CustomDataConverter converter = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.Configure<DurableTaskWorkerOptions>("test", x => x.DataConverter = converter);
        DefaultDurableTaskBuilder builder = new("test", services)
        {
            BuildTarget = typeof(GoodBuildTarget),
        };

        IHostedService service = builder.Build(services.BuildServiceProvider());
        GoodBuildTarget target = service.Should().BeOfType<GoodBuildTarget>().Subject;
        target.Name.Should().Be("test");
        target.Factory.Should().NotBeNull();
        target.Options.Should().NotBeNull();
        target.Options.DataConverter.Should().BeSameAs(converter);
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

    class CustomDataConverter : DataConverter
    {
        public override object? Deserialize(string? data, Type targetType)
        {
            throw new NotImplementedException();
        }

        public override string? Serialize(object? value)
        {
            throw new NotImplementedException();
        }
    }
}
