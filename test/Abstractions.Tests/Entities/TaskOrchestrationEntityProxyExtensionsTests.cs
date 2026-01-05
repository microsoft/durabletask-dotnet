// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Moq;

namespace Microsoft.DurableTask.Tests.Entities;

public class TaskOrchestrationEntityProxyExtensionsTests
{
    [Fact]
    public void CreateProxy_NullFeature_ThrowsArgumentNullException()
    {
        // Arrange, Act, Assert
        TaskOrchestrationEntityFeature feature = null!;
        EntityInstanceId id = new("TestEntity", "key1");

        Action act = () => feature.CreateProxy<ITestEntityProxy>(id);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CallMethod_WithTaskResult_CallsCallEntityAsync()
    {
        // Arrange
        Mock<TaskOrchestrationEntityFeature> mockFeature = new();
        EntityInstanceId id = new("TestEntity", "key1");
        int expectedResult = 42;

        mockFeature
            .Setup(f => f.CallEntityAsync<int>(id, "GetValue", null, null))
            .ReturnsAsync(expectedResult);

        ITestEntityProxy proxy = mockFeature.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        int result = await proxy.GetValue();

        // Assert
        result.Should().Be(expectedResult);
        mockFeature.Verify(
            f => f.CallEntityAsync<int>(id, "GetValue", null, null),
            Times.Once);
    }

    [Fact]
    public async Task CallMethod_WithTaskResultAndInput_CallsCallEntityAsyncWithInput()
    {
        // Arrange
        Mock<TaskOrchestrationEntityFeature> mockFeature = new();
        EntityInstanceId id = new("TestEntity", "key1");
        int input = 5;
        int expectedResult = 47;

        mockFeature
            .Setup(f => f.CallEntityAsync<int>(id, "Add", input, null))
            .ReturnsAsync(expectedResult);

        ITestEntityProxy proxy = mockFeature.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        int result = await proxy.Add(input);

        // Assert
        result.Should().Be(expectedResult);
        mockFeature.Verify(
            f => f.CallEntityAsync<int>(id, "Add", input, null),
            Times.Once);
    }

    [Fact]
    public async Task CallMethod_WithTaskNoResult_CallsCallEntityAsync()
    {
        // Arrange
        Mock<TaskOrchestrationEntityFeature> mockFeature = new();
        EntityInstanceId id = new("TestEntity", "key1");

        mockFeature
            .Setup(f => f.CallEntityAsync(id, "Reset", null, null))
            .Returns(Task.CompletedTask);

        ITestEntityProxy proxy = mockFeature.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        await proxy.Reset();

        // Assert
        mockFeature.Verify(
            f => f.CallEntityAsync(id, "Reset", null, null),
            Times.Once);
    }

    [Fact]
    public void CallMethod_VoidReturn_CallsSignalEntityAsync()
    {
        // Arrange
        Mock<TaskOrchestrationEntityFeature> mockFeature = new();
        EntityInstanceId id = new("TestEntity", "key1");

        mockFeature
            .Setup(f => f.SignalEntityAsync(id, "Delete", null, null))
            .Returns(Task.CompletedTask);

        ITestEntityProxy proxy = mockFeature.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        proxy.Delete();

        // Assert
        mockFeature.Verify(
            f => f.SignalEntityAsync(id, "Delete", null, null),
            Times.Once);
    }

    [Fact]
    public async Task CallMethod_WithMultipleParameters_PassesParametersAsArray()
    {
        // Arrange
        Mock<TaskOrchestrationEntityFeature> mockFeature = new();
        EntityInstanceId id = new("TestEntity", "key1");
        string param1 = "test";
        int param2 = 42;

        mockFeature
            .Setup(f => f.CallEntityAsync<string>(
                id,
                "Combine",
                It.Is<object?[]>(arr => arr != null && arr.Length == 2 && (string)arr[0]! == param1 && (int)arr[1]! == param2),
                null))
            .ReturnsAsync("result");

        ITestEntityProxy proxy = mockFeature.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        string result = await proxy.Combine(param1, param2);

        // Assert
        result.Should().Be("result");
        mockFeature.Verify(
            f => f.CallEntityAsync<string>(
                id,
                "Combine",
                It.Is<object?[]>(arr => arr != null && arr.Length == 2),
                null),
            Times.Once);
    }

    [Fact]
    public void CreateProxy_WithEntityNameAndKey_CreatesProxyWithCorrectId()
    {
        // Arrange
        Mock<TaskOrchestrationEntityFeature> mockFeature = new();
        string entityName = "TestEntity";
        string entityKey = "key1";

        mockFeature
            .Setup(f => f.SignalEntityAsync(
                It.Is<EntityInstanceId>(id => id.Name == entityName.ToLowerInvariant() && id.Key == entityKey),
                "Delete",
                null,
                null))
            .Returns(Task.CompletedTask);

        // Act
        ITestEntityProxy proxy = mockFeature.Object.CreateProxy<ITestEntityProxy>(entityName, entityKey);
        proxy.Delete();

        // Assert
        mockFeature.Verify(
            f => f.SignalEntityAsync(
                It.Is<EntityInstanceId>(id => id.Name == entityName.ToLowerInvariant() && id.Key == entityKey),
                "Delete",
                null,
                null),
            Times.Once);
    }

    public interface ITestEntityProxy : IEntityProxy
    {
        Task<int> GetValue();

        Task<int> Add(int value);

        Task Reset();

        void Delete();

        Task<string> Combine(string str, int num);
    }
}
