// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                UnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.WhenNoExactMatch,
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
