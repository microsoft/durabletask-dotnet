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
    public void SubOrchestrationOptions_InstanceId_Overriden()
    {
        string instanceId = Guid.NewGuid().ToString();
        SubOrchestrationOptions subOptions = new(new TaskOptions(), instanceId);
        subOptions.Retry.Should().BeNull();
        subOptions.Tags.Should().BeNull();
        subOptions.InstanceId.Should().BeNull();

        StartOrchestrationOptions startOptions = new();
        startOptions.Version.Should().BeNull();
        startOptions.InstanceId.Should().BeNull();
        startOptions.StartAt.Should().BeNull();
        startOptions.Tags.Should().BeEmpty();
    }
}
