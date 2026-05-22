// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Tests;

public class DurableTaskFactoryActivityVersioningTests
{
    [Fact]
    public void TryCreateActivity_WithMatchingVersion_ReturnsMatchingImplementation()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<InvoiceActivityV2>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<InvoiceActivityV2>();
    }

    [Fact]
    public void TryCreateActivity_WithoutMatchingVersion_ReturnsFalse()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeFalse();
        activity.Should().BeNull();
    }

    [Fact]
    public void TryCreateActivity_WithRequestedVersion_UsesUnversionedRegistrationWhenAvailable()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<UnversionedInvoiceActivity>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<UnversionedInvoiceActivity>();
    }

    [Fact]
    public void TryCreateActivity_WithMixedRegistrations_PrefersExactVersionMatch()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<InvoiceActivityV2>();
        registry.AddActivity<UnversionedInvoiceActivity>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v1"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<InvoiceActivityV1>();
    }

    [Fact]
    public void PublicTryCreateActivity_UsesUnversionedRegistrationOnly()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<UnversionedInvoiceActivity>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = factory.TryCreateActivity(
            new TaskName("InvoiceActivity"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<UnversionedInvoiceActivity>();
    }

    [Fact]
    public void TryCreateActivity_WithMixedRegistrations_DoesNotFallBackToUnversionedWhenAnotherVersionIsRegistered()
    {
        // Arrange: register an unversioned activity and a v1 activity, then request v2.
        // Because the name has at least one versioned registration, the unversioned registration must NOT
        // be used as a fallback for the unmatched v2 request.
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<UnversionedInvoiceActivity>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeFalse();
        activity.Should().BeNull();
    }

    [Fact]
    public void TryCreateActivity_WithMixedRegistrationsAndUnversionedFallback_UsesUnversionedRegistrationForUnknownVersion()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<UnversionedInvoiceActivity>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<UnversionedInvoiceActivity>();
    }

    [Fact]
    public void TryCreateActivity_WithOrchestratorFallbackOnly_DoesNotEnableActivityFallback()
    {
        // Arrange — only the orchestrator-side flag is enabled. Activity dispatch must still be closed-set
        // for mixed names; the split into two properties must isolate the two sides independently.
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<UnversionedInvoiceActivity>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v9"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeFalse();
        activity.Should().BeNull();
    }

    [Fact]
    public void TryCreateActivity_WithActivityFallback_LogsDispatchAtDebug()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<UnversionedInvoiceActivity>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        CapturingLoggerFactory loggerFactory = new();
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions, loggerFactory);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v9"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<UnversionedInvoiceActivity>();
        loggerFactory.Logs.Should().Contain(log =>
            log.Level == LogLevel.Debug
            && log.Message.Contains("InvoiceActivity", StringComparison.Ordinal)
            && log.Message.Contains("v9", StringComparison.Ordinal)
            && log.Message.Contains("unversioned", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryCreateActivity_StrictExactOnlyWithOnlyUnversionedRegistration_RejectsVersionedRequest()
    {
        // Arrange — only the unversioned registration exists. Under Implicit, a versioned request would
        // resolve to it. StrictExactOnly disables that path: the request must fail.
        DurableTaskRegistry registry = new();
        registry.AddActivity<UnversionedInvoiceActivity>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v1"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeFalse();
        activity.Should().BeNull();
    }

    [Fact]
    public void TryCreateActivity_StrictExactOnlyWithOnlyUnversionedRegistration_AcceptsUnversionedRequest()
    {
        // Arrange — exact-match path for unversioned requests is preserved.
        DurableTaskRegistry registry = new();
        registry.AddActivity<UnversionedInvoiceActivity>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = factory.TryCreateActivity(
            new TaskName("InvoiceActivity"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<UnversionedInvoiceActivity>();
    }

    [Fact]
    public void TryCreateActivity_OrchestratorStrictExactOnly_DoesNotAffectActivityFallback()
    {
        // Arrange — asymmetric explicit modes: orchestrator side StrictExactOnly, activity side CatchAll.
        // Activity registry is mixed ([X] + [X v=1]). Versioned request for v9 must fall back to the
        // unversioned activity per CatchAll on the activity side; the orchestrator-side flag must NOT leak.
        DurableTaskRegistry registry = new();
        registry.AddActivity<InvoiceActivityV1>();
        registry.AddActivity<UnversionedInvoiceActivity>();
        DurableTaskWorkerOptions workerOptions = new()
        {
            Versioning = new DurableTaskWorkerOptions.VersioningOptions
            {
                OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.StrictExactOnly,
                ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            },
        };
        IDurableTaskFactory factory = registry.BuildFactory(workerOptions);

        // Act
        bool found = ((IVersionedTaskFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v9"),
            Mock.Of<IServiceProvider>(),
            out ITaskActivity? activity);

        // Assert
        found.Should().BeTrue();
        activity.Should().BeOfType<UnversionedInvoiceActivity>();
    }

    [DurableTask("InvoiceActivity", Version = "v1")]
    sealed class InvoiceActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("InvoiceActivity", Version = "v2")]
    sealed class InvoiceActivityV2 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("v2");
    }

    [DurableTask("InvoiceActivity")]
    sealed class UnversionedInvoiceActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("unversioned");
    }
}
