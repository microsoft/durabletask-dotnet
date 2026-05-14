// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tests;

public class DurableTaskAttributeVersionTests
{
    [Fact]
    public void DurableTaskAttribute_VersionProperty_PreservesValue()
    {
        // Arrange
        DurableTaskAttribute attribute = new("MyTask") { Version = "v2" };

        // Assert
        attribute.Name.Name.Should().Be("MyTask");
        attribute.Version.Should().Be("v2");
    }

    [Fact]
    public void GetDurableTaskVersion_WithVersionedAttribute_ReturnsVersion()
    {
        // Arrange
        Type type = typeof(VersionedTestOrchestrator);

        // Act
        TaskVersion version = type.GetDurableTaskVersion();

        // Assert
        version.Version.Should().Be("v1");
    }

    [Fact]
    public void GetDurableTaskVersion_WithoutVersion_ReturnsDefault()
    {
        // Arrange
        Type type = typeof(UnversionedTestOrchestrator);

        // Act
        TaskVersion version = type.GetDurableTaskVersion();

        // Assert
        version.Should().Be(default(TaskVersion));
    }

    [DurableTask(Version = "v1")]
    sealed class VersionedTestOrchestrator : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult(input);
    }

    sealed class UnversionedTestOrchestrator : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult(input);
    }
}
