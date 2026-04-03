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

    [Fact]
    public void AddActivity_SameLogicalNameDifferentVersions_DoesNotThrow()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        Action act = () =>
        {
            registry.AddActivity<ShippingActivityV1>();
            registry.AddActivity<ShippingActivityV2>();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AddActivity_SameLogicalNameAndVersion_Throws()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        Action act = () =>
        {
            registry.AddActivity<DuplicateShippingActivityV1>();
            registry.AddActivity<DuplicateShippingActivityV1Copy>();
        };

        // Assert
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddActivity_ExplicitVersionFactory_SameLogicalNameDifferentVersions_DoesNotThrow()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        Action act = () =>
        {
            registry.AddActivity("ManualActivity", new TaskVersion("v1"), () => new ManualActivity("v1"));
            registry.AddActivity("ManualActivity", new TaskVersion("v2"), () => new ManualActivity("v2"));
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

    [DurableTask("ShippingActivity")]
    [DurableTaskVersion("v1")]
    sealed class ShippingActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("ShippingActivity")]
    [DurableTaskVersion("v2")]
    sealed class ShippingActivityV2 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("v2");
    }

    [DurableTask("DuplicateActivity")]
    [DurableTaskVersion("v1")]
    sealed class DuplicateShippingActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("DuplicateActivity")]
    [DurableTaskVersion("v1")]
    sealed class DuplicateShippingActivityV1Copy : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
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

    sealed class ManualActivity : TaskActivity<string, string>
    {
        readonly string marker;

        public ManualActivity(string marker)
        {
            this.marker = marker;
        }

        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult(this.marker);
    }
}
