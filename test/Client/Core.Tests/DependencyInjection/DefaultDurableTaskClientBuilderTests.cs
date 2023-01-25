// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client.Tests;

public class DefaultDurableTaskClientBuilderTests
{
    [Fact]
    public void BuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskClientBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(BadBuildTarget);
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void BuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskClientBuilder builder = new("test", new ServiceCollection());
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
        DefaultDurableTaskClientBuilder builder = new("test", services);
        Action act = () => builder.Build(services.BuildServiceProvider());
        act.Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void Build_Target_Built()
    {
        CustomDataConverter converter = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.Configure<DurableTaskClientOptions>("test", x => x.DataConverter = converter);
        DefaultDurableTaskClientBuilder builder = new("test", services)
        {
            BuildTarget = typeof(GoodBuildTarget),
        };

        DurableTaskClient client = builder.Build(services.BuildServiceProvider());
        GoodBuildTarget target = client.Should().BeOfType<GoodBuildTarget>().Subject;
        target.Name.Should().Be("test");
        target.Options.Should().NotBeNull();
        target.Options.DataConverter.Should().BeSameAs(converter);
    }

    class BadBuildTarget
    {
    }

    class GoodBuildTarget : DurableTaskClient
    {
        public GoodBuildTarget(string name, IOptionsMonitor<DurableTaskClientOptions> options)
            : base(name)
        {
            this.Options = options.Get(name);
        }

        public new string Name => base.Name;

        public DurableTaskClientOptions Options { get; }

        public override ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata?> GetInstanceMetadataAsync(
            string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override AsyncPageable<OrchestrationMetadata> GetInstanceMetadataAsync(OrchestrationQuery? query = null)
        {
            throw new NotImplementedException();
        }

        public override Task<PurgeResult> PurgeInstanceMetadataAsync(
            string instanceId, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<PurgeResult> PurgeInstanceMetadataAsync(
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
