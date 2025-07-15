// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.Tests;

public class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MyBuilder")]
    public void AddDurableTaskWorker_SameInstance(string? name)
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder actual1 = services.AddDurableTaskWorker(name);
        IDurableTaskWorkerBuilder actual2 = services.AddDurableTaskWorker(name);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskWorker_SameInstance2()
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder? actual1 = null;
        IDurableTaskWorkerBuilder? actual2 = null;
        services.AddDurableTaskWorker(builder => actual1 = builder);
        services.AddDurableTaskWorker(builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MyBuilder")]
    public void AddDurableTaskWorker_SameInstance3(string name)
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder? actual1 = null;
        IDurableTaskWorkerBuilder? actual2 = null;
        services.AddDurableTaskWorker(name, builder => actual1 = builder);
        services.AddDurableTaskWorker(name, builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskWorker_SameInstance4()
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder actual1 = services.AddDurableTaskWorker();
        IDurableTaskWorkerBuilder? actual2 = null;
        services.AddDurableTaskWorker(builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskWorker_DifferentNames_NotSame()
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder actual1 = services.AddDurableTaskWorker();
        IDurableTaskWorkerBuilder actual2 = services.AddDurableTaskWorker("MyBuilder");

        actual1.Should().NotBeNull();
        actual2.Should().NotBeNull();
        actual1.Should().NotBeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskWorker_DifferentNames_NotSame2()
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder? actual1 = null;
        IDurableTaskWorkerBuilder? actual2 = null;
        services.AddDurableTaskWorker(builder => actual1 = builder);
        services.AddDurableTaskWorker("MyBuilder", builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual2.Should().NotBeNull();
        actual1.Should().NotBeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskWorker_DifferentNames_NotSame3()
    {
        ServiceCollection services = new();
        IDurableTaskWorkerBuilder actual1 = services.AddDurableTaskWorker();
        IDurableTaskWorkerBuilder? actual2 = null;
        services.AddDurableTaskWorker("MyBuilder", builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual2.Should().NotBeNull();
        actual1.Should().NotBeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskWorker_HostedServiceAdded()
    {
        ServiceCollection services = new();
        services.AddDurableTaskWorker(builder => { });
        services.Should().ContainSingle(x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddDurableTaskWorker_ConfiguresConverter()
    {
        ServiceCollection services = new();
        CustomDataConverter converter = new();
        services.AddSingleton<DataConverter>(converter);
        services.AddDurableTaskWorker(builder => { });

        DurableTaskWorkerOptions options = services.BuildServiceProvider().GetOptions<DurableTaskWorkerOptions>();
        options.DataConverter.Should().Be(converter);
    }

    [Fact]
    public void AddDurableTaskWorker_DoesNotConfiguresConverter()
    {
        ServiceCollection services = new();
        CustomDataConverter converter = new();
        services.AddSingleton<DataConverter>(converter);
        services.AddDurableTaskWorker(builder => builder.Configure(x => x.DataConverter = JsonDataConverter.Default ));

        DurableTaskWorkerOptions options = services.BuildServiceProvider().GetOptions<DurableTaskWorkerOptions>();
        options.DataConverter.Should().BeSameAs(JsonDataConverter.Default);
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
