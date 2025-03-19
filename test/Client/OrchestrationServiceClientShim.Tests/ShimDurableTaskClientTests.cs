// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using FluentAssertions.Specialized;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Converters;
using Microsoft.Extensions.Options;
using Core = DurableTask.Core;
using CoreOrchestrationQuery = DurableTask.Core.Query.OrchestrationQuery;
using PurgeInstanceFilter = Microsoft.DurableTask.Client.PurgeInstancesFilter;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim.Tests;

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

    [Fact]
    public void Ctor_NoEntitySupport_GetClientThrows()
    {
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        ShimDurableTaskClientOptions options = new() { Client = client };
        ShimDurableTaskClient shimClient = new("test", options);

        Func<DurableEntityClient> act = () => shimClient.Entities;
        act.Should().ThrowExactly<InvalidOperationException>().WithMessage("Entity support is not enabled.");
    }

    [Fact]
    public void Ctor_InvalidEntityOptions_GetClientThrows()
    {
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        ShimDurableTaskClientOptions options = new() { Client = client, EnableEntitySupport = true };
        ShimDurableTaskClient shimClient = new("test", options);

        Func<DurableEntityClient> act = () => shimClient.Entities;
        act.Should().ThrowExactly<NotSupportedException>()
            .WithMessage("The configured IOrchestrationServiceClient does not support entities.");
    }

    [Fact]
    public void Ctor_EntitiesConfigured_GetClientSuccess()
    {
        IOrchestrationServiceClient client = Mock.Of<IOrchestrationServiceClient>();
        EntityBackendQueries queries = Mock.Of<EntityBackendQueries>();
        ShimDurableTaskClientOptions options = new()
        {
            Client = client,
            EnableEntitySupport = true,
            Entities = { Queries = queries },
        };

        ShimDurableTaskClient shimClient = new("test", options);
        DurableEntityClient entityClient = shimClient.Entities;

        entityClient.Should().BeOfType<ShimDurableEntityClient>();
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
        OrchestrationMetadata? result = await this.client.GetInstanceAsync(instanceId, false);

        // assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstanceMetadata_Results(bool getInputs)
    {
        // arrange
        List<Core.OrchestrationState> states = new() { CreateState("input") };
        string instanceId = states.First().OrchestrationInstance.InstanceId;
        this.orchestrationClient.Setup(m => m.GetOrchestrationStateAsync(instanceId, false)).ReturnsAsync(states);

        // act
        OrchestrationMetadata? result = await this.client.GetInstanceAsync(instanceId, getInputs);

        // assert
        Validate(result, states.First(), getInputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetInstances_Results(bool getInputs)
    {
        // arrange
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        List<Core.OrchestrationState> states =
        [
            CreateState("input", start: utcNow.AddMinutes(-1)),
            CreateState(10, "output", utcNow.AddMinutes(-5)),
        ];

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
        List<OrchestrationMetadata> result = await this.client.GetAllInstancesAsync(query).ToListAsync();

        // assert
        this.orchestrationClient.VerifyAll();
        foreach ((OrchestrationMetadata left, Core.OrchestrationState right) in result.Zip(states))
        {
            Validate(left, right, getInputs);
        }
    }

    [Fact]
    public async Task PurgeInstanceMetadata()
    {
        // arrange
        string instanceId = Guid.NewGuid().ToString();
        this.purgeClient.Setup(m => m.PurgeInstanceStateAsync(instanceId)).ReturnsAsync(new Core.PurgeResult(1));

        // act
        PurgeResult result = await this.client.PurgeInstanceAsync(instanceId);

        // assert
        this.orchestrationClient.VerifyAll();
        result.PurgedInstanceCount.Should().Be(1);
    }

    [Fact]
    public async Task PurgeInstances()
    {
        // arrange
        PurgeInstanceFilter filter = new(CreatedTo: DateTimeOffset.UtcNow);
        this.purgeClient.Setup(m => m.PurgeInstanceStateAsync(It.IsNotNull<Core.PurgeInstanceFilter>()))
            .ReturnsAsync(new Core.PurgeResult(10));

        // act
        PurgeResult result = await this.client.PurgeAllInstancesAsync(filter);

        // assert
        this.orchestrationClient.VerifyAll();
        result.PurgedInstanceCount.Should().Be(10);
    }

    [Fact]
    public async Task RaiseEvent()
    {
        // arrange
        string instanceId = Guid.NewGuid().ToString();
        this.SetupClientTaskMessage<EventRaisedEvent>(instanceId);

        // act
        await this.client.RaiseEventAsync(instanceId, "test-event", null, default);

        // assert
        this.orchestrationClient.VerifyAll();
    }

    [Fact]
    public async Task SuspendInstance()
    {
        // arrange
        string instanceId = Guid.NewGuid().ToString();
        this.SetupClientTaskMessage<ExecutionSuspendedEvent>(instanceId);

        // act
        await this.client.SuspendInstanceAsync(instanceId, null, default);

        // assert
        this.orchestrationClient.VerifyAll();
    }

    [Fact]
    public async Task ResumeInstance()
    {
        // arrange
        string instanceId = Guid.NewGuid().ToString();
        this.SetupClientTaskMessage<ExecutionResumedEvent>(instanceId);

        // act
        await this.client.ResumeInstanceAsync(instanceId, null, default);

        // assert
        this.orchestrationClient.VerifyAll();
    }

    [Fact]
    public async Task TerminateInstance()
    {
        // arrange
        string instanceId = Guid.NewGuid().ToString();
        this.orchestrationClient.Setup(m => m.ForceTerminateTaskOrchestrationAsync(instanceId, null))
            .Returns(Task.CompletedTask);

        // act
        await this.client.TerminateInstanceAsync(instanceId, null, default);

        // assert
        this.orchestrationClient.VerifyAll();
    }

    [Fact]
    public async Task WaitForInstanceCompletion()
    {
        // arrange
        Core.OrchestrationState state = CreateState("input", "output");
        this.orchestrationClient.Setup(
            m => m.WaitForOrchestrationAsync(state.OrchestrationInstance.InstanceId, null, TimeSpan.MaxValue, default))
            .ReturnsAsync(state);

        // act
        OrchestrationMetadata metadata = await this.client.WaitForInstanceCompletionAsync(
            state.OrchestrationInstance.InstanceId, false, default);

        // assert
        this.orchestrationClient.VerifyAll();
        Validate(metadata, state, false);
    }

    [Fact]
    public async Task WaitForInstanceStart()
    {
        // arrange
        DateTimeOffset start = DateTimeOffset.UtcNow;
        OrchestrationInstance instance = new()
        {
            InstanceId = Guid.NewGuid().ToString(),
            ExecutionId = Guid.NewGuid().ToString(),
        };

        Core.OrchestrationState state1 = CreateState("input", start: start);
        state1.OrchestrationInstance = instance;
        state1.OrchestrationStatus = Core.OrchestrationStatus.Pending;
        Core.OrchestrationState state2 = CreateState("input", start: start);
        state1.OrchestrationInstance = instance;
        this.orchestrationClient.SetupSequence(m => m.GetOrchestrationStateAsync(instance.InstanceId, false))
            .ReturnsAsync([state1])
            .ReturnsAsync([state2]);

        // act
        OrchestrationMetadata metadata = await this.client.WaitForInstanceStartAsync(
            instance.InstanceId, false, default);

        // assert
        this.orchestrationClient.Verify(
            m => m.GetOrchestrationStateAsync(instance.InstanceId, false), Times.Exactly(2));
        Validate(metadata, state2, false);
    }

    [Fact]
    public Task ScheduleNewOrchestrationInstance_IdGenerated_NoInput()
        => this.RunScheduleNewOrchestrationInstanceAsync("test", null, null);

    [Fact]
    public Task ScheduleNewOrchestrationInstance_IdProvided_InputProvided()
        => this.RunScheduleNewOrchestrationInstanceAsync("test", "input", new() { InstanceId = "test-id" });

    [Fact]
    public Task ScheduleNewOrchestrationInstance_StartAt()
        => this.RunScheduleNewOrchestrationInstanceAsync(
            "test", null, new() { StartAt = DateTimeOffset.UtcNow.AddHours(1) });

    [Fact]
    public async Task ScheduleNewOrchestrationInstance_IdProvided_TagsProvided()
    {
        StartOrchestrationOptions options = new()
        {
            InstanceId = "test-id",
            Tags = new Dictionary<string, string>()
            {
                { "tag1", "value1" },
                { "tag2", "value2" }
            }
        };

        await this.RunScheduleNewOrchestrationInstanceAsync("test", "input", options);
    }


    static Core.OrchestrationState CreateState(
        object input, object? output = null, DateTimeOffset start = default)
    {
        string? serializedOutput = null;
        FailureDetails? failure = null;
        OrchestrationStatus status = OrchestrationStatus.Running;

        if (start == default)
        {
            start = DateTimeOffset.UtcNow.AddMinutes(-10);
        }

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

    static TaskMessage MatchStartExecutionMessage(TaskName name, object? input, StartOrchestrationOptions? options)
    {
        return Match.Create<TaskMessage>(m =>
        {
            if (m.Event is not ExecutionStartedEvent @event)
            {
                return false;
            }


            if (options?.InstanceId is string str && m.OrchestrationInstance.InstanceId != str)
            {
                return false;
            }
            else if (options?.InstanceId is null && !Guid.TryParse(m.OrchestrationInstance.InstanceId, out _))
            {
                return false;
            }

            if (options?.StartAt is DateTimeOffset start && @event.ScheduledStartTime != start.UtcDateTime)
            {
                return false;
            }
            else if (options?.StartAt is null && @event.ScheduledStartTime is not null)
            {
                return false;
            }

            return Guid.TryParse(m.OrchestrationInstance.ExecutionId, out _)
                && @event.Name == name.Name && @event.Version == name.Version
                && @event.OrchestrationInstance == m.OrchestrationInstance
                && @event.EventId == -1
                && @event.Input == JsonDataConverter.Default.Serialize(input);
        });
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

    void SetupClientTaskMessage<TEvent>(string instanceId)
        where TEvent : HistoryEvent
    {
        this.orchestrationClient
            .Setup(m => m.SendTaskOrchestrationMessageAsync(It.Is<TaskMessage>(m =>
                m.OrchestrationInstance.InstanceId == instanceId && m.Event.GetType() == typeof(TEvent))
            ))
            .Returns(Task.CompletedTask);
    }

    async Task RunScheduleNewOrchestrationInstanceAsync(
        TaskName name, object? input, StartOrchestrationOptions? options)
    {
        // arrange
        this.orchestrationClient.Setup(
            m => m.CreateTaskOrchestrationAsync(MatchStartExecutionMessage(name, input, options)))
            .Returns(Task.CompletedTask);

        // act
        string instanceId = await this.client.ScheduleNewOrchestrationInstanceAsync(name, input, options, default);

        // assert
        this.orchestrationClient.Verify(
            m => m.CreateTaskOrchestrationAsync(MatchStartExecutionMessage(name, input, options)),
            Times.Once());

        if (options?.InstanceId is string str)
        {
            instanceId.Should().Be(str);
        }
        else
        {
            Guid.TryParse(instanceId, out _).Should().BeTrue();
        }
    }
}