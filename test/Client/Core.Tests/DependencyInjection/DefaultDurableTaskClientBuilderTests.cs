// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

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

        public override AsyncPageable<OrchestrationMetadata> GetInstances(OrchestrationQuery? query = null)
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
