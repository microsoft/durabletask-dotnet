// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Client.Tests;

public class ServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MyBuilder")]
    public void AddDurableTaskClient_SameInstance(string? name)
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder actual1 = services.AddDurableTaskClient(name);
        IDurableTaskClientBuilder actual2 = services.AddDurableTaskClient(name);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskClient_SameInstance2()
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder? actual1 = null;
        IDurableTaskClientBuilder? actual2 = null;
        services.AddDurableTaskClient(builder => actual1 = builder);
        services.AddDurableTaskClient(builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MyBuilder")]
    public void AddDurableTaskClient_SameInstance3(string name)
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder? actual1 = null;
        IDurableTaskClientBuilder? actual2 = null;
        services.AddDurableTaskClient(name, builder => actual1 = builder);
        services.AddDurableTaskClient(name, builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskClient_SameInstance4()
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder actual1 = services.AddDurableTaskClient();
        IDurableTaskClientBuilder? actual2 = null;
        services.AddDurableTaskClient(builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual1.Should().BeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskClient_DifferentNames_NotSame()
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder actual1 = services.AddDurableTaskClient();
        IDurableTaskClientBuilder actual2 = services.AddDurableTaskClient("MyBuilder");

        actual1.Should().NotBeNull();
        actual2.Should().NotBeNull();
        actual1.Should().NotBeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskClient_DifferentNames_NotSame2()
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder? actual1 = null;
        IDurableTaskClientBuilder? actual2 = null;
        services.AddDurableTaskClient(builder => actual1 = builder);
        services.AddDurableTaskClient("MyBuilder", builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual2.Should().NotBeNull();
        actual1.Should().NotBeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskClient_DifferentNames_NotSame3()
    {
        ServiceCollection services = new();
        IDurableTaskClientBuilder actual1 = services.AddDurableTaskClient();
        IDurableTaskClientBuilder? actual2 = null;
        services.AddDurableTaskClient("MyBuilder", builder => actual2 = builder);

        actual1.Should().NotBeNull();
        actual2.Should().NotBeNull();
        actual1.Should().NotBeSameAs(actual2);
    }

    [Fact]
    public void AddDurableTaskClient_HostedServiceAdded()
    {
        ServiceCollection services = new();
        services.AddDurableTaskClient(builder => { });
        services.Should().ContainSingle(
            x => x.ServiceType == typeof(IDurableTaskClientProvider) && x.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(
            x => x.ServiceType == typeof(DurableTaskClient) && x.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddDurableTaskClient_Named_HostedServiceAdded()
    {
        ServiceCollection services = new();
        services.AddDurableTaskClient("named", builder => { });
        services.Should().ContainSingle(
            x => x.ServiceType == typeof(IDurableTaskClientProvider) && x.Lifetime == ServiceLifetime.Singleton);
        services.Should().NotContain(
            x => x.ServiceType == typeof(DurableTaskClient) && x.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddDurableTaskClient_ConfiguresConverter()
    {
        ServiceCollection services = new();
        CustomDataConverter converter = new();
        services.AddSingleton<DataConverter>(converter);
        services.AddDurableTaskClient(builder => { });

        DurableTaskClientOptions options = services.BuildServiceProvider().GetOptions<DurableTaskClientOptions>();
        options.DataConverter.Should().Be(converter);
    }

    [Fact]
    public void AddDurableTaskClient_DoesNotConfiguresConverter()
    {
        ServiceCollection services = new();
        CustomDataConverter converter = new();
        services.AddSingleton<DataConverter>(converter);
        services.AddDurableTaskClient(builder => builder.Configure(x => x.DataConverter = JsonDataConverter.Default ));

        DurableTaskClientOptions options = services.BuildServiceProvider().GetOptions<DurableTaskClientOptions>();
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
