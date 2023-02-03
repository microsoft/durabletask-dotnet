// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public void UseBuildTargetT_ValidTypeWithOptions_Sets()
    {
        JsonDataConverter converter = new();
        ServiceCollection services = new();
        DefaultDurableTaskClientBuilder builder = new("test", services);
        builder.Configure(opt => opt.DataConverter = converter);
        builder.UseBuildTarget<GoodBuildTarget, GoodBuildTargetOptions>();
        DurableTaskClient client = builder.Build(services.BuildServiceProvider());

        GoodBuildTarget target = client.Should().BeOfType<GoodBuildTarget>().Subject;
        target.Name.Should().Be("test");
        target.Options.Should().NotBeNull();
        target.Options.DataConverter.Should().BeSameAs(converter);
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
        public GoodBuildTarget(string name, IOptionsMonitor<GoodBuildTargetOptions> options)
            : base(name)
        {
            this.Options = options.Get(name);
        }

        public new string Name => base.Name;

        public GoodBuildTargetOptions Options { get; }

        public override ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata?> GetInstancesAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
        {
            throw new NotImplementedException();
        }

        public override Task<PurgeResult> PurgeInstanceAsync(
            string instanceId, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<PurgeResult> PurgeAllInstancesAsync(
            PurgeInstancesFilter filter, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task RaiseEventAsync(
            string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task ResumeInstanceAsync(
            string instanceId, string? reason = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(
            TaskName orchestratorName,
            object? input = null,
            StartOrchestrationOptions? options = null,
            CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task SuspendInstanceAsync(
            string instanceId, string? reason = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task TerminateInstanceAsync(
            string instanceId, object? output = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }
    }

    class GoodBuildTargetOptions : DurableTaskClientOptions
    {
    }
}
