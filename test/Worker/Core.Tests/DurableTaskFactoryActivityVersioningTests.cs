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
        bool found = ((IVersionedActivityFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            allowVersionFallback: true,
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
        bool found = ((IVersionedActivityFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            allowVersionFallback: true,
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
        bool found = ((IVersionedActivityFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            allowVersionFallback: true,
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
        bool found = ((IVersionedActivityFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v1"),
            Mock.Of<IServiceProvider>(),
            allowVersionFallback: true,
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
    public void TryCreateActivity_WithRequestedVersion_DoesNotUseUnversionedRegistrationWhenFallbackIsDisallowed()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<UnversionedInvoiceActivity>();
        IDurableTaskFactory factory = registry.BuildFactory();

        // Act
        bool found = ((IVersionedActivityFactory)factory).TryCreateActivity(
            new TaskName("InvoiceActivity"),
            new TaskVersion("v2"),
            Mock.Of<IServiceProvider>(),
            allowVersionFallback: false,
            out ITaskActivity? activity);

        // Assert
        found.Should().BeFalse();
        activity.Should().BeNull();
    }

    [DurableTask("InvoiceActivity")]
    [DurableTaskVersion("v1")]
    sealed class InvoiceActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
            => Task.FromResult("v1");
    }

    [DurableTask("InvoiceActivity")]
    [DurableTaskVersion("v2")]
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
