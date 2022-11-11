// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Client.Tests;

public class DurableTaskClientBuilderExtensionsTests
{
    [Fact]
    public void UseBuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskClientBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget(typeof(BadBuildTarget));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void UseBuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskClientBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget(typeof(GoodBuildTarget));
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void UseBuildTargetT_ValidType_Sets()
    {
        DefaultDurableTaskClientBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget<GoodBuildTarget>();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void Configure_ConfiguresOptions()
    {
        ServiceCollection services = new();
        DefaultDurableTaskClientBuilder builder = new("test", services);

        DurableTaskClientOptions? actual = null;
        builder.Configure(options => actual = options);
        DurableTaskClientOptions expected = services.BuildServiceProvider()
            .GetOptions<DurableTaskClientOptions>("test");

        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void RegisterDirectly_AddsSingleton()
    {
        ServiceCollection services = new();
        services.AddDurableTaskClient("test", b =>
        {
            b.UseBuildTarget<GoodBuildTarget>();
            b.RegisterDirectly();
        });

        ServiceDescriptor descriptor = services.FirstOrDefault(x => x.ServiceType == typeof(DurableTaskClient))!;
        descriptor.Should().NotBeNull();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        IServiceProvider serviceProvider = services.BuildServiceProvider();
        DurableTaskClient client = serviceProvider.GetRequiredService<DurableTaskClient>();
        client.Should().NotBeNull();
        client.Should().BeOfType<GoodBuildTarget>();
        client.Name.Should().Be("test");
    }

    class BadBuildTarget
    {
    }

    class GoodBuildTarget : DurableTaskClient
    {
        public GoodBuildTarget(string name, DurableTaskClientOptions options)
            : base(name, options)
        {
        }

        public new string Name => base.Name;

        public new DurableTaskClientOptions Options => base.Options;

        public override ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata?> GetInstanceMetadataAsync(
            string instanceId, bool getInputsAndOutputs)
        {
            throw new NotImplementedException();
        }

        public override AsyncPageable<OrchestrationMetadata> GetInstances(
            OrchestrationQuery? query = null)
        {
            throw new NotImplementedException();
        }

        public override Task<PurgeResult> PurgeInstanceMetadataAsync(
            string instanceId, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<PurgeResult> PurgeInstancesAsync(
            PurgeInstancesFilter filter, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task RaiseEventAsync(
            string instanceId, string eventName, object? eventPayload)
        {
            throw new NotImplementedException();
        }

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(
            TaskName orchestratorName,
            object? input = null,
            StartOrchestrationOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public override Task TerminateAsync(string instanceId, object? output)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
            string instanceId, CancellationToken cancellationToken, bool getInputsAndOutputs = false)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
            string instanceId, CancellationToken cancellationToken, bool getInputsAndOutputs = false)
        {
            throw new NotImplementedException();
        }
    }
}
