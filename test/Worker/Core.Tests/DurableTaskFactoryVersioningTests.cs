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
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
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
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
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
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<UnversionedInvoiceWorkflow>();
    }

    [Fact]
    public void TryCreateOrchestrator_WithMixedRegistrations_PrefersExactVersionMatch()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        registry.AddOrchestrator<InvoiceWorkflowV2>();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v1"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<InvoiceWorkflowV1>();
    }

    [Fact]
    public void TryCreateOrchestrator_WithMixedRegistrations_DoesNotFallBackForUnknownVersion()
    {
        // Arrange — name "InvoiceWorkflow" has both versioned (v1, v2) and unversioned registrations.
        // A request for v3 (no exact match) must NOT silently fall back to the unversioned registration:
        // doing so would route the call to a different implementation than the caller asked for.
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        registry.AddOrchestrator<InvoiceWorkflowV2>();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v3"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeFalse();
        orchestrator.Should().BeNull();
    }

    [Fact]
    public void TryCreateOrchestrator_WithOnlyUnversionedRegistration_FallsBackForVersionedRequest()
    {
        // Arrange — name "InvoiceWorkflow" has only the unversioned registration. A versioned request
        // is allowed to fall back to it (migration path: pre-versioning instances scheduled with
        // a specific version against a registry that hasn't migrated yet).
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v1"),
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

    [Fact]
    public void TryCreateActivity_UnversionedAndMultiVersionRegistrations_ServesEveryDeclaredVersion()
    {
        // Arrange — an "original" unversioned activity registered with a bare [DurableTask] attribute
        // coexists with a newer class that declares multiple versions in one attribute
        // ([DurableTask("InvoiceActivity", Version = "1.0.0,1.1.0")]). All three logical endpoints —
        // the unversioned original plus each comma-separated version — must be independently servable.
        DurableTaskRegistry registry = new();
        registry.AddActivity<OriginalInvoiceActivity>();
        registry.AddActivity<MultiVersionInvoiceActivity>();
        IVersionedTaskFactory factory = (IVersionedTaskFactory)registry.BuildFactory();

        // Act
        bool unversionedFound = factory.TryCreateActivity(
            new TaskName("InvoiceActivity"),
            default,
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? unversionedActivity);
        bool v1Found = factory.TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("1.0.0"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? v1Activity);
        bool v11Found = factory.TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("1.1.0"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? v11Activity);

        // Assert — the unversioned request resolves to the original, and each declared version resolves
        // to the multi-version class.
        unversionedFound.Should().BeTrue();
        unversionedActivity.Should().BeOfType<OriginalInvoiceActivity>();
        v1Found.Should().BeTrue();
        v1Activity.Should().BeOfType<MultiVersionInvoiceActivity>();
        v11Found.Should().BeTrue();
        v11Activity.Should().BeOfType<MultiVersionInvoiceActivity>();
    }

    [DurableTask("InvoiceWorkflow", Version = "v1")]
    sealed class InvoiceWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("InvoiceWorkflow", Version = "v2")]
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

    [DurableTask("InvoiceActivity")]
    sealed class OriginalInvoiceActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("original");
    }

    [DurableTask("InvoiceActivity", Version = "1.0.0,1.1.0")]
    sealed class MultiVersionInvoiceActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult(context.Version);
    }
}
