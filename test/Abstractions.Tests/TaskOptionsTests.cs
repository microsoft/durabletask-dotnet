// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tests;

public class TaskOptionsTests
{
    [Fact]
    public void Empty_Ctors_Okay()
    {
        TaskOptions options = new();
        options.Retry.Should().BeNull();
        options.Tags.Should().BeNull();

        SubOrchestrationOptions subOptions = new();
        subOptions.Retry.Should().BeNull();
        subOptions.Tags.Should().BeNull();
        subOptions.InstanceId.Should().BeNull();

        StartOrchestrationOptions startOptions = new();
        startOptions.Version.Should().BeNull();
        startOptions.InstanceId.Should().BeNull();
        startOptions.StartAt.Should().BeNull();
        startOptions.Tags.Should().BeEmpty();
    }

    [Fact]
    public void SubOrchestrationOptions_InstanceId_Correct()
    {
        string instanceId = Guid.NewGuid().ToString();
        SubOrchestrationOptions subOptions = new(new TaskOptions(), instanceId);
        instanceId.Equals(subOptions.InstanceId).Should().BeTrue();

        string subInstanceId = Guid.NewGuid().ToString();
        subOptions = new(new SubOrchestrationOptions(instanceId: subInstanceId));
        subInstanceId.Equals(subOptions.InstanceId).Should().BeTrue();

        subOptions = new(new SubOrchestrationOptions(instanceId: subInstanceId), instanceId);
        instanceId.Equals(subOptions.InstanceId).Should().BeTrue();
    }
}
