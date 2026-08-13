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

    [Fact]
    public void GetDurableTaskVersion_WhitespaceVersion_ThrowsArgumentException()
    {
        // Arrange — reflection path picks up the whitespace-only Version, hands it to TaskVersion's
        // constructor which fails closed. This guards code that calls AddOrchestrator(typeof(...)) or
        // AddActivity(typeof(...)) on a type whose attribute the source generator did not see (e.g.,
        // assemblies that opted out of the generator).
        Type type = typeof(WhitespaceVersionedTestOrchestrator);

        // Act
        Action act = () => type.GetDurableTaskVersion();

        // Assert
        act.Should().ThrowExactly<ArgumentException>()
            .WithMessage("*whitespace*");
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

    [DurableTask("WhitespaceVersioned", Version = "   ")]
    sealed class WhitespaceVersionedTestOrchestrator : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
            => Task.FromResult(input);
    }
}
