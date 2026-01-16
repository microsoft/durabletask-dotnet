// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

public class OrchestrationIdReusePolicyTests
{
    [Fact]
    public void OrchestrationIdReusePolicy_Error_CreatesCorrectPolicy()
    {
        // Arrange & Act
        OrchestrationIdReusePolicy policy = OrchestrationIdReusePolicy.Error(
            OrchestrationRuntimeStatus.Running,
            OrchestrationRuntimeStatus.Pending);

        // Assert
        policy.Action.Should().Be(CreateOrchestrationAction.Error);
        policy.OperationStatuses.Should().HaveCount(2);
        policy.OperationStatuses.Should().Contain(OrchestrationRuntimeStatus.Running);
        policy.OperationStatuses.Should().Contain(OrchestrationRuntimeStatus.Pending);
    }

    [Fact]
    public void OrchestrationIdReusePolicy_Ignore_CreatesCorrectPolicy()
    {
        // Arrange & Act
        OrchestrationIdReusePolicy policy = OrchestrationIdReusePolicy.Ignore(
            OrchestrationRuntimeStatus.Completed,
            OrchestrationRuntimeStatus.Failed);

        // Assert
        policy.Action.Should().Be(CreateOrchestrationAction.Ignore);
        policy.OperationStatuses.Should().HaveCount(2);
        policy.OperationStatuses.Should().Contain(OrchestrationRuntimeStatus.Completed);
        policy.OperationStatuses.Should().Contain(OrchestrationRuntimeStatus.Failed);
    }

    [Fact]
    public void OrchestrationIdReusePolicy_Terminate_CreatesCorrectPolicy()
    {
        // Arrange & Act
        OrchestrationIdReusePolicy policy = OrchestrationIdReusePolicy.Terminate(
            OrchestrationRuntimeStatus.Running);

        // Assert
        policy.Action.Should().Be(CreateOrchestrationAction.Terminate);
        policy.OperationStatuses.Should().HaveCount(1);
        policy.OperationStatuses.Should().Contain(OrchestrationRuntimeStatus.Running);
    }

    [Fact]
    public void ConvertToProtoReusePolicy_NullPolicy_ReturnsNull()
    {
        // Arrange
        OrchestrationIdReusePolicy? policy = null;

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertToProtoReusePolicy(policy);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertToProtoReusePolicy_ErrorAction_ConvertsCorrectly()
    {
        // Arrange
        OrchestrationIdReusePolicy policy = OrchestrationIdReusePolicy.Error(
            OrchestrationRuntimeStatus.Running,
            OrchestrationRuntimeStatus.Pending);

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertToProtoReusePolicy(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Action.Should().Be(P.CreateOrchestrationAction.Error);
        result.OperationStatus.Should().HaveCount(2);
        result.OperationStatus.Should().Contain(P.OrchestrationStatus.Running);
        result.OperationStatus.Should().Contain(P.OrchestrationStatus.Pending);
    }

    [Fact]
    public void ConvertToProtoReusePolicy_IgnoreAction_ConvertsCorrectly()
    {
        // Arrange
        OrchestrationIdReusePolicy policy = OrchestrationIdReusePolicy.Ignore(
            OrchestrationRuntimeStatus.Completed);

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertToProtoReusePolicy(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Action.Should().Be(P.CreateOrchestrationAction.Ignore);
        result.OperationStatus.Should().HaveCount(1);
        result.OperationStatus.Should().Contain(P.OrchestrationStatus.Completed);
    }

    [Fact]
    public void ConvertToProtoReusePolicy_TerminateAction_ConvertsCorrectly()
    {
        // Arrange
        OrchestrationIdReusePolicy policy = OrchestrationIdReusePolicy.Terminate(
            OrchestrationRuntimeStatus.Failed,
            OrchestrationRuntimeStatus.Terminated);

        // Act
        P.OrchestrationIdReusePolicy? result = ProtoUtils.ConvertToProtoReusePolicy(policy);

        // Assert
        result.Should().NotBeNull();
        result!.Action.Should().Be(P.CreateOrchestrationAction.Terminate);
        result.OperationStatus.Should().HaveCount(2);
        result.OperationStatus.Should().Contain(P.OrchestrationStatus.Failed);
        result.OperationStatus.Should().Contain(P.OrchestrationStatus.Terminated);
    }

    [Fact]
    public void ConvertToProtoAction_AllActions_ConvertCorrectly()
    {
        // Assert - Error
        ProtoUtils.ConvertToProtoAction(CreateOrchestrationAction.Error)
            .Should().Be(P.CreateOrchestrationAction.Error);

        // Assert - Ignore
        ProtoUtils.ConvertToProtoAction(CreateOrchestrationAction.Ignore)
            .Should().Be(P.CreateOrchestrationAction.Ignore);

        // Assert - Terminate
        ProtoUtils.ConvertToProtoAction(CreateOrchestrationAction.Terminate)
            .Should().Be(P.CreateOrchestrationAction.Terminate);
    }

    [Fact]
    public void WithIdReusePolicy_SetsPolicy()
    {
        // Arrange
        var options = new StartOrchestrationOptions();
        var policy = OrchestrationIdReusePolicy.Terminate(OrchestrationRuntimeStatus.Running);

        // Act
        StartOrchestrationOptions result = options.WithIdReusePolicy(policy);

        // Assert
        result.IdReusePolicy.Should().Be(policy);
    }
}
