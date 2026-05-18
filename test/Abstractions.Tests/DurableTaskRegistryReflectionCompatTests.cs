// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Tests;

/// <summary>
/// Verifies that <see cref="DurableTaskRegistry"/> preserves the binary-compatibility shape relied on
/// by the currently shipped <c>Microsoft.Azure.Functions.Worker.Extensions.DurableTask</c> 1.4.0
/// extension. The extension uses reflection
/// (<see cref="BindingFlags.Instance"/> | <see cref="BindingFlags.NonPublic"/>) to read the internal
/// <c>Orchestrators</c>, <c>Activities</c>, and <c>Entities</c> members and casts each value to
/// <see cref="IEnumerable{T}"/> of
/// <see cref="KeyValuePair{TKey, TValue}"/> of <see cref="TaskName"/> and the corresponding factory delegate.
/// </summary>
public class DurableTaskRegistryReflectionCompatTests
{
    const BindingFlags ExtensionFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Orchestrators_ReflectedInternalProperty_CastsToTaskNameKeyedEnumerable()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<RegistryReflectionWorkflowOne>();
        registry.AddOrchestrator<RegistryReflectionWorkflowTwo>();

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Orchestrators", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>>)raw!;

        // Assert
        cast.Should().HaveCount(2);
        cast.Select(kvp => kvp.Key.Name).Should().BeEquivalentTo(
            new[] { nameof(RegistryReflectionWorkflowOne), nameof(RegistryReflectionWorkflowTwo) });
    }

    [Fact]
    public void Activities_ReflectedInternalProperty_CastsToTaskNameKeyedEnumerable()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<RegistryReflectionActivityOne>();
        registry.AddActivity<RegistryReflectionActivityTwo>();

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Activities", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>>)raw!;

        // Assert
        cast.Should().HaveCount(2);
        cast.Select(kvp => kvp.Key.Name).Should().BeEquivalentTo(
            new[] { nameof(RegistryReflectionActivityOne), nameof(RegistryReflectionActivityTwo) });
    }

    [Fact]
    public void Entities_ReflectedInternalProperty_CastsToTaskNameKeyedEnumerable()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddEntity("Counter", _ => Mock.Of<ITaskEntity>());
        registry.AddEntity("Inventory", _ => Mock.Of<ITaskEntity>());

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Entities", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskEntity>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskEntity>>>)raw!;

        // Assert
        cast.Should().HaveCount(2);
        cast.Select(kvp => kvp.Key.Name).Should().BeEquivalentTo(new[] { "Counter", "Inventory" });
    }

    [Fact]
    public void Orchestrators_ReflectedInternalProperty_MultiVersionRegistry_EmitsOneEntryPerRegistration()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<MultiVersionWorkflowV1>();
        registry.AddOrchestrator<MultiVersionWorkflowV2>();

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Orchestrators", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>>)raw!;

        // Assert
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> entries = cast.ToList();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(kvp => kvp.Key.Name == "MultiVersionWorkflow");
        entries.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Activities_ReflectedInternalProperty_MultiVersionRegistry_EmitsOneEntryPerRegistration()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<MultiVersionActivityV1>();
        registry.AddActivity<MultiVersionActivityV2>();

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Activities", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>>)raw!;

        // Assert
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> entries = cast.ToList();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(kvp => kvp.Key.Name == "MultiVersionActivity");
        entries.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Orchestrators_ReflectedInternalProperty_MixedUnversionedAndVersioned_EmitsAllEntries()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator("MixedWorkflow", () => Mock.Of<ITaskOrchestrator>());
        registry.AddOrchestrator("MixedWorkflow", new TaskVersion("v1"), () => Mock.Of<ITaskOrchestrator>());

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Orchestrators", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>>)raw!;

        // Assert
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> entries = cast.ToList();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(kvp => kvp.Key.Name == "MixedWorkflow");
        entries.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Activities_ReflectedInternalProperty_MixedUnversionedAndVersioned_EmitsAllEntries()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity("MixedActivity", _ => Mock.Of<ITaskActivity>());
        registry.AddActivity("MixedActivity", new TaskVersion("v1"), () => Mock.Of<ITaskActivity>());

        // Act
        object? raw = typeof(DurableTaskRegistry).GetProperty("Activities", ExtensionFlags)!.GetValue(registry);
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> cast =
            (IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>>)raw!;

        // Assert
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> entries = cast.ToList();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(kvp => kvp.Key.Name == "MixedActivity");
        entries.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [DurableTask]
    sealed class RegistryReflectionWorkflowOne : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("one");
    }

    [DurableTask]
    sealed class RegistryReflectionWorkflowTwo : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("two");
    }

    [DurableTask]
    sealed class RegistryReflectionActivityOne : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("one");
    }

    [DurableTask]
    sealed class RegistryReflectionActivityTwo : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("two");
    }

    [DurableTask("MultiVersionWorkflow", Version = "v1")]
    sealed class MultiVersionWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("v1");
    }

    [DurableTask("MultiVersionWorkflow", Version = "v2")]
    sealed class MultiVersionWorkflowV2 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("v2");
    }

    [DurableTask("MultiVersionActivity", Version = "v1")]
    sealed class MultiVersionActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("v1");
    }

    [DurableTask("MultiVersionActivity", Version = "v2")]
    sealed class MultiVersionActivityV2 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("v2");
    }
}
