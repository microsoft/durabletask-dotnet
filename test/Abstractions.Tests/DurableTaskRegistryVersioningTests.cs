// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tests;

public class DurableTaskRegistryVersioningTests
{
    [Fact]
    public void AddOrchestrator_SameLogicalNameDifferentVersions_DoesNotThrow()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        Action act = () =>
        {
            registry.AddOrchestrator<ShippingWorkflowV1>();
            registry.AddOrchestrator<ShippingWorkflowV2>();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AddOrchestrator_SameLogicalNameAndVersion_Throws()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        Action act = () =>
        {
            registry.AddOrchestrator<DuplicateShippingWorkflowV1>();
            registry.AddOrchestrator<DuplicateShippingWorkflowV1Copy>();
        };

        // Assert
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddOrchestrator_ExplicitVersionFactory_SameLogicalNameDifferentVersions_DoesNotThrow()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        Action act = () =>
        {
            registry.AddOrchestrator("ManualWorkflow", new TaskVersion("v1"), () => new ManualWorkflow("v1"));
            registry.AddOrchestrator("ManualWorkflow", new TaskVersion("v2"), () => new ManualWorkflow("v2"));
        };

        // Assert
        act.Should().NotThrow();
    }

    [DurableTask("ShippingWorkflow")]
    [DurableTaskVersion("v1")]
    sealed class ShippingWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("ShippingWorkflow")]
    [DurableTaskVersion("v2")]
    sealed class ShippingWorkflowV2 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v2");
    }

    [DurableTask("DuplicateWorkflow")]
    [DurableTaskVersion("v1")]
    sealed class DuplicateShippingWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("DuplicateWorkflow")]
    [DurableTaskVersion("v1")]
    sealed class DuplicateShippingWorkflowV1Copy : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v1-copy");
    }

    sealed class ManualWorkflow : TaskOrchestrator<string, string>
    {
        readonly string marker;

        public ManualWorkflow(string marker)
        {
            this.marker = marker;
        }

        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult(this.marker);
    }
}
