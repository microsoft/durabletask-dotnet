// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Tests;

/// <summary>
/// Verifies the public <see cref="DurableTaskRegistry.GetOrchestrators"/>,
/// <see cref="DurableTaskRegistry.GetActivities"/>, and <see cref="DurableTaskRegistry.GetEntities"/>
/// enumeration API.
/// </summary>
public class DurableTaskRegistryPublicEnumerationTests
{
    [Fact]
    public void GetOrchestrators_Empty_ReturnsEmpty()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> actual = registry.GetOrchestrators();

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void GetActivities_Empty_ReturnsEmpty()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> actual = registry.GetActivities();

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void GetEntities_Empty_ReturnsEmpty()
    {
        // Arrange
        DurableTaskRegistry registry = new();

        // Act
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskEntity>>> actual = registry.GetEntities();

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void GetOrchestrators_ReturnsRegisteredOrchestrators()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<PublicEnumOrchestratorOne>();
        registry.AddOrchestrator<PublicEnumOrchestratorTwo>();

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> actual = registry.GetOrchestrators().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Select(kvp => kvp.Key.Name).Should().BeEquivalentTo(
            new[] { nameof(PublicEnumOrchestratorOne), nameof(PublicEnumOrchestratorTwo) });
        actual.Should().OnlyContain(kvp => kvp.Value != null);
    }

    [Fact]
    public void GetActivities_ReturnsRegisteredActivities()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<PublicEnumActivityOne>();
        registry.AddActivity<PublicEnumActivityTwo>();

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> actual = registry.GetActivities().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Select(kvp => kvp.Key.Name).Should().BeEquivalentTo(
            new[] { nameof(PublicEnumActivityOne), nameof(PublicEnumActivityTwo) });
        actual.Should().OnlyContain(kvp => kvp.Value != null);
    }

    [Fact]
    public void GetEntities_ReturnsRegisteredEntities()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddEntity("Counter", _ => Mock.Of<ITaskEntity>());
        registry.AddEntity("Inventory", _ => Mock.Of<ITaskEntity>());

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskEntity>>> actual = registry.GetEntities().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Select(kvp => kvp.Key.Name).Should().BeEquivalentTo(new[] { "Counter", "Inventory" });
        actual.Should().OnlyContain(kvp => kvp.Value != null);
    }

    [Fact]
    public void GetOrchestrators_MultiVersion_EmitsOneEntryPerRegistration()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator<PublicEnumWorkflowV1>();
        registry.AddOrchestrator<PublicEnumWorkflowV2>();

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> actual = registry.GetOrchestrators().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Should().OnlyContain(kvp => kvp.Key.Name == "PublicEnumWorkflow");
        actual.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetActivities_MultiVersion_EmitsOneEntryPerRegistration()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity<PublicEnumActivityV1>();
        registry.AddActivity<PublicEnumActivityV2>();

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> actual = registry.GetActivities().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Should().OnlyContain(kvp => kvp.Key.Name == "PublicEnumActivity");
        actual.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetOrchestrators_MixedUnversionedAndVersioned_EmitsAllEntries()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddOrchestrator("MixedPublicWorkflow", () => Mock.Of<ITaskOrchestrator>());
        registry.AddOrchestrator("MixedPublicWorkflow", new TaskVersion("v1"), () => Mock.Of<ITaskOrchestrator>());

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskOrchestrator>>> actual = registry.GetOrchestrators().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Should().OnlyContain(kvp => kvp.Key.Name == "MixedPublicWorkflow");
        actual.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetActivities_MixedUnversionedAndVersioned_EmitsAllEntries()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddActivity("MixedPublicActivity", _ => Mock.Of<ITaskActivity>());
        registry.AddActivity("MixedPublicActivity", new TaskVersion("v1"), () => Mock.Of<ITaskActivity>());

        // Act
        List<KeyValuePair<TaskName, Func<IServiceProvider, ITaskActivity>>> actual = registry.GetActivities().ToList();

        // Assert
        actual.Should().HaveCount(2);
        actual.Should().OnlyContain(kvp => kvp.Key.Name == "MixedPublicActivity");
        actual.Select(kvp => kvp.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetEntities_ReturnedEnumerable_CannotBeDowncastToMutateRegistry()
    {
        // Arrange
        DurableTaskRegistry registry = new();
        registry.AddEntity("Counter", _ => Mock.Of<ITaskEntity>());

        // Act
        IEnumerable<KeyValuePair<TaskName, Func<IServiceProvider, ITaskEntity>>> result = registry.GetEntities();

        // Assert
        (result as IDictionary<TaskName, Func<IServiceProvider, ITaskEntity>>).Should().BeNull();
    }

    [DurableTask]
    sealed class PublicEnumOrchestratorOne : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("one");
    }

    [DurableTask]
    sealed class PublicEnumOrchestratorTwo : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("two");
    }

    [DurableTask]
    sealed class PublicEnumActivityOne : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("one");
    }

    [DurableTask]
    sealed class PublicEnumActivityTwo : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("two");
    }

    [DurableTask("PublicEnumWorkflow", Version = "v1")]
    sealed class PublicEnumWorkflowV1 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("v1");
    }

    [DurableTask("PublicEnumWorkflow", Version = "v2")]
    sealed class PublicEnumWorkflowV2 : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input) => Task.FromResult("v2");
    }

    [DurableTask("PublicEnumActivity", Version = "v1")]
    sealed class PublicEnumActivityV1 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("v1");
    }

    [DurableTask("PublicEnumActivity", Version = "v2")]
    sealed class PublicEnumActivityV2 : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult("v2");
    }
}
