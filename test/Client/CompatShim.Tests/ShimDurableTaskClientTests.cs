// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Query;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.Options;
using Core = DurableTask.Core;
using CoreOrchestrationQuery = DurableTask.Core.Query.OrchestrationQuery;
using PurgeInstanceFilter = Microsoft.DurableTask.Client.PurgeInstancesFilter;

namespace Microsoft.DurableTask.Client.CompatShim.Tests;

public class ShimDurableTaskClientTests
{
    readonly ShimDurableTaskClient client;
    readonly Mock<IOrchestrationServiceClient> orchestrationClient = new(MockBehavior.Strict);
    readonly Mock<IOrchestrationServiceQueryClient> queryClient;
    readonly Mock<IOrchestrationServicePurgeClient> purgeClient;

    public ShimDurableTaskClientTests()
    {
        this.queryClient = this.orchestrationClient.As<IOrchestrationServiceQueryClient>();
        this.purgeClient = this.orchestrationClient.As<IOrchestrationServicePurgeClient>();
        this.client = new("test", new ShimDurableTaskClientOptions { Client = this.orchestrationClient.Object });
    }

    [Fact]
    public void Ctor_NullOptions_Throws1()
    {
        IOptionsMonitor<ShimDurableTaskClientOptions> options = null!;
        Func<ShimDurableTaskClient> act = () => new ShimDurableTaskClient("test", options);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("options");

        options = Mock.Of<IOptionsMonitor<ShimDurableTaskClientOptions>>();
        act = () => new ShimDurableTaskClient("test", options);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Ctor_NullOptions_Throws2()
    {
        IOptionsMonitor<ShimDurableTaskClientOptions> options =
            Mock.Of<IOptionsMonitor<ShimDurableTaskClientOptions>>();
        Func<ShimDurableTaskClient> act = () => new ShimDurableTaskClient("test", options);
        act.Should().ThrowExactly<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async void GetInstanceMetadata_EmptyList_Null(bool isNull)
    {
        // arrange
        List<Core.OrchestrationState>? states = isNull ? null : new();
        string instanceId = Guid.NewGuid().ToString();
        this.orchestrationClient.Setup(m => m.GetOrchestrationStateAsync(instanceId, false)).ReturnsAsync(states);

        // act
        OrchestrationMetadata? result = await this.client.GetInstanceMetadataAsync(instanceId, false);

        // assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstanceMetadata_Results_Success(bool getInputs)
    {
        // arrange
        List<Core.OrchestrationState> states = new() { CreateState("input") };
        string instanceId = states.First().OrchestrationInstance.InstanceId;
        this.orchestrationClient.Setup(m => m.GetOrchestrationStateAsync(instanceId, false)).ReturnsAsync(states);

        // act
        OrchestrationMetadata? result = await this.client.GetInstanceMetadataAsync(instanceId, getInputs);

        // assert
        Validate(result, states.First(), getInputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstances_Results_Success(bool getInputs)
    {
        // arrange
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        List<Core.OrchestrationState> states = new()
        {
            CreateState("input", start: utcNow.AddMinutes(-1)),
            CreateState(10, "output", utcNow.AddMinutes(-5)),
        };

        OrchestrationQueryResult queryResult = new(states, null);
        string instanceId = states.First().OrchestrationInstance.InstanceId;
        this.queryClient
            .Setup(m => m.GetOrchestrationWithQueryAsync(It.IsNotNull<CoreOrchestrationQuery>(), default))
            .ReturnsAsync(queryResult);

        OrchestrationQuery query = new()
        {
            CreatedFrom = utcNow.AddMinutes(-10),
            FetchInputsAndOutputs = getInputs,
        };

        // act
        List<OrchestrationMetadata> result = await this.client.GetInstances(query).ToListAsync();

        // assert
        foreach ((OrchestrationMetadata left, Core.OrchestrationState right) in result.Zip(states))
        {
            Validate(left, right, getInputs);
        }
    }

    [Fact]
    public async Task PurgeInstanceMetadata_Success()
    {
        // arrange
        string instanceId = Guid.NewGuid().ToString();
        this.purgeClient.Setup(m => m.PurgeInstanceStateAsync(instanceId)).ReturnsAsync(new Core.PurgeResult(1));

        // act
        PurgeResult result = await this.client.PurgeInstanceMetadataAsync(instanceId);

        // assert
        result.PurgedInstanceCount.Should().Be(1);
    }

    [Fact]
    public async Task PurgeInstances_Success()
    {
        // arrange
        PurgeInstanceFilter filter = new(CreatedTo: DateTimeOffset.UtcNow);
        this.purgeClient.Setup(m => m.PurgeInstanceStateAsync(It.IsNotNull<Core.PurgeInstanceFilter>()))
            .ReturnsAsync(new Core.PurgeResult(10));

        // act
        PurgeResult result = await this.client.PurgeInstancesAsync(filter);

        // assert
        result.PurgedInstanceCount.Should().Be(10);
    }

    static Core.OrchestrationState CreateState(
        object input, object? output = null, DateTimeOffset start = default)
    {
        string? serializedOutput = null;
        FailureDetails? failure = null;
        OrchestrationStatus status = OrchestrationStatus.Running;

        switch (output)
        {
            case Exception ex:
                status = OrchestrationStatus.Failed;
                failure = new(ex.GetType().FullName!, ex.Message, ex.StackTrace, null, true);
                break;
            case not null:
                status = OrchestrationStatus.Completed;
                serializedOutput = JsonDataConverter.Default.Serialize(output);
                break;
        }

        return new()
        {
            CompletedTime = default,
            CreatedTime = start.UtcDateTime,
            LastUpdatedTime = start.AddMinutes(10).UtcDateTime,
            Input = JsonDataConverter.Default.Serialize(input),
            Output = serializedOutput,
            FailureDetails = failure,
            Name = "test-orchestration",
            OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            },
            OrchestrationStatus = status,
            Status = JsonDataConverter.Default.Serialize("custom-status"),
            Version = string.Empty,
        };
    }

    static void Validate(OrchestrationMetadata? metadata, Core.OrchestrationState? state, bool getInputs)
    {
        if (state is null)
        {
            metadata.Should().BeNull();
            return;
        }

        metadata.Should().NotBeNull();
        metadata!.Name.Should().Be(state.Name);
        metadata.InstanceId.Should().Be(state.OrchestrationInstance.InstanceId);
        metadata.RuntimeStatus.Should().Be(state.OrchestrationStatus.ConvertFromCore());
        metadata.CreatedAt.Should().Be(new DateTimeOffset(state.CreatedTime));
        metadata.LastUpdatedAt.Should().Be(new DateTimeOffset(state.LastUpdatedTime));
        metadata.SerializedInput.Should().Be(state.Input);
        metadata.SerializedOutput.Should().Be(state.Output);
        metadata.SerializedCustomStatus.Should().Be(state.Status);

        if (getInputs)
        {
            metadata.Invoking(m => m.ReadInputAs<object>()).Should().NotThrow();
        }
    }

    static void Validate(TaskFailureDetails? left, FailureDetails? right)
    {
        if (right is null)
        {
            left.Should().BeNull();
            return;
        }

        left.Should().NotBeNull();
        left!.ErrorType.Should().Be(right.ErrorType);
        left.ErrorMessage.Should().Be(right.ErrorMessage);
        left.StackTrace.Should().Be(right.StackTrace);
        Validate(left.InnerFailure, right.InnerFailure);
    }
}