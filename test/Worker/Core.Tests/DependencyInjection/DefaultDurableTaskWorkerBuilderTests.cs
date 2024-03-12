// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Tests;

public class DefaultDurableTaskWorkerBuilderTests
{
    [Fact]
    public void BuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(BadBuildTarget);
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void BuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(GoodBuildTarget);
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));

        builder.BuildTarget = null;
        builder.BuildTarget.Should().BeNull();
    }

    [Fact]
    public void UseBuildTargetT_ValidType_Sets()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget<GoodBuildTarget>();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void UseBuildTargetT_ValidTypeWithOptions_Sets()
    {
        JsonDataConverter converter = new();
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.Configure(opt => opt.DataConverter = converter);
        builder.UseBuildTarget<GoodBuildTarget, GoodBuildTargetOptions>();
        IHostedService client = builder.Build(services.BuildServiceProvider());

        GoodBuildTarget target = client.Should().BeOfType<GoodBuildTarget>().Subject;
        target.Name.Should().Be("test");
        target.Options.Should().NotBeNull();
        target.Options.DataConverter.Should().BeSameAs(converter);
    }

    [Fact]
    public void Build_NoTarget_Throws()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
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
        DefaultDurableTaskWorkerBuilder builder = new("test", services)
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

    class GoodBuildTarget(
        string name, DurableTaskFactory factory, IOptionsMonitor<DurableTaskWorkerOptions> options)
        : DurableTaskWorker(name, factory)
    {
        public new string Name => base.Name;

        public new IDurableTaskFactory Factory => base.Factory;

        public DurableTaskWorkerOptions Options { get; } = options.Get(name);

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

    class GoodBuildTargetOptions : DurableTaskWorkerOptions
    {
    }
}
