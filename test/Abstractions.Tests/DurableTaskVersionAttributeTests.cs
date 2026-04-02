// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tests;

using System.Reflection;

public class DurableTaskVersionAttributeTests
{
    [Fact]
    public void Ctor_WithVersion_PreservesTaskVersion()
    {
        // Arrange
        DurableTaskVersionAttribute attribute = new("v2");

        // Act
        string? version = attribute.Version.Version;

        // Assert
        version.Should().Be("v2");
    }

    [Fact]
    public void GetDurableTaskVersion_WithAttribute_ReturnsVersion()
    {
        // Arrange
        Type type = typeof(VersionedTestOrchestrator);

        // Act
        TaskVersion version = GetDurableTaskVersion(type);

        // Assert
        version.Version.Should().Be("v1");
    }

    [Fact]
    public void GetDurableTaskVersion_WithoutAttribute_ReturnsDefault()
    {
        // Arrange
        Type type = typeof(UnversionedTestOrchestrator);

        // Act
        TaskVersion version = GetDurableTaskVersion(type);

        // Assert
        version.Should().Be(default(TaskVersion));
    }

    [DurableTaskVersion("v1")]
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

    static TaskVersion GetDurableTaskVersion(Type type)
    {
        MethodInfo method = typeof(TaskName).Assembly
            .GetType("Microsoft.DurableTask.TypeExtensions", throwOnError: true)!
            .GetMethod("GetDurableTaskVersion", BindingFlags.Static | BindingFlags.NonPublic)!;

        return (TaskVersion)method.Invoke(null, new object[] { type })!;
    }
}
