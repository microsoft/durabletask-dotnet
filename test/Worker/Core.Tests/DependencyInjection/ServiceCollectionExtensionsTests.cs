// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddDurableTaskWorker_SameInstance()
    {
        ServiceCollection services = new();
        IDurableTaskBuilder? actual1 = null;
        IDurableTaskBuilder? actual2 = null;
        services.AddDurableTaskWorker(builder => actual1 = builder);
        services.AddDurableTaskWorker(builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
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