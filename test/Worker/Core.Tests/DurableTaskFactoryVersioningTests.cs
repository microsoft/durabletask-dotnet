// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Tests;

public class DurableTaskFactoryVersioningTests
{
    [Fact]
    public void TryCreateOrchestrator_WithMatchingVersion_ReturnsMatchingImplementation()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        registry.AddOrchestrator<InvoiceWorkflowV2>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedOrchestratorFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<InvoiceWorkflowV2>();
    }

    [Fact]
    public void TryCreateOrchestrator_WithoutMatchingVersion_ReturnsFalse()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedOrchestratorFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeFalse();
        orchestrator.Should().BeNull();
    }

    [Fact]
    public void TryCreateOrchestrator_WithRequestedVersion_UsesUnversionedRegistrationWhenAvailable()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedOrchestratorFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<UnversionedInvoiceWorkflow>();
    }

    [Fact]
    public void PublicTryCreateOrchestrator_UsesUnversionedRegistrationOnly()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = factory.TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<UnversionedInvoiceWorkflow>();
    }

    [DurableTask("InvoiceWorkflow")]
    [DurableTaskVersion("v1")]
    sealed class InvoiceWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("InvoiceWorkflow")]
    [DurableTaskVersion("v2")]
    sealed class InvoiceWorkflowV2 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v2");
    }

    [DurableTask("InvoiceWorkflow")]
    sealed class UnversionedInvoiceWorkflow : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("unversioned");
    }
}
