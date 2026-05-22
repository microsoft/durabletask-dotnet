// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

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
    public void TryCreateOrchestrator_WithMixedRegistrationsAndUnversionedFallback_UsesUnversionedRegistrationForUnknownVersion()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        registry.AddOrchestrator<InvoiceWorkflowV2>();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v3"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<UnversionedInvoiceWorkflow>();
    }

    [Fact]
    public void TryCreateOrchestrator_WithActivityFallbackOnly_DoesNotEnableOrchestratorFallback()
    {
        // Arrange — only the activity-side flag is enabled. Orchestrator dispatch must still be closed-set
        // for mixed names; otherwise the split into two properties does not actually isolate the two sides.
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v9"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeFalse();
        orchestrator.Should().BeNull();
    }

    [Fact]
    public void TryCreateOrchestrator_WithOrchestratorFallback_LogsDispatchAtDebug()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<InvoiceWorkflowV1>();
        registry.AddOrchestrator<UnversionedInvoiceWorkflow>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        CapturingLoggerFactory loggerFactory = new();
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions, loggerFactory);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateOrchestrator(
            new TaskName("InvoiceWorkflow"),
            new TaskVersion("v9"),
            Mock.Of<IServiceProvider>(),
            out ITaskOrchestrator? orchestrator);

        // Assert
        found.Should().BeTrue();
        orchestrator.Should().BeOfType<UnversionedInvoiceWorkflow>();
        loggerFactory.Logs.Should().Contain(log =>
            log.Level == LogLevel.Debug
            && log.Message.Contains("InvoiceWorkflow", StringComparison.Ordinal)
            && log.Message.Contains("v9", StringComparison.Ordinal)
            && log.Message.Contains("unversioned", StringComparison.OrdinalIgnoreCase));
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
}
