// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Moq;

namespace Microsoft.DurableTask.Client.Tests.Entities;

public class DurableEntityClientProxyExtensionsTests
{
    [Fact]
    public void CreateProxy_NullClient_ThrowsArgumentNullException()
    {
        // Arrange, Act, Assert
        DurableEntityClient client = null!;
        EntityInstanceId id = new("TestEntity", "key1");

        Action act = () => client.CreateProxy<ITestEntityProxy>(id);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CallMethod_WithTask_CallsSignalEntityAsync()
    {
        // Arrange
        Mock<DurableEntityClient> mockClient = new("test");
        EntityInstanceId id = new("TestEntity", "key1");

        mockClient
            .Setup(c => c.SignalEntityAsync(id, "Reset", null, null, default))
            .Returns(Task.CompletedTask);

        ITestEntityProxy proxy = mockClient.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        await proxy.Reset();

        // Assert
        mockClient.Verify(
            c => c.SignalEntityAsync(id, "Reset", null, null, default),
            Times.Once);
    }

    [Fact]
    public async Task CallMethod_WithTaskAndInput_CallsSignalEntityAsyncWithInput()
    {
        // Arrange
        Mock<DurableEntityClient> mockClient = new("test");
        EntityInstanceId id = new("TestEntity", "key1");
        int input = 5;

        mockClient
            .Setup(c => c.SignalEntityAsync(id, "Add", input, null, default))
            .Returns(Task.CompletedTask);

        ITestEntityProxy proxy = mockClient.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        await proxy.Add(input);

        // Assert
        mockClient.Verify(
            c => c.SignalEntityAsync(id, "Add", input, null, default),
            Times.Once);
    }

    [Fact]
    public void CallMethod_VoidReturn_CallsSignalEntityAsync()
    {
        // Arrange
        Mock<DurableEntityClient> mockClient = new("test");
        EntityInstanceId id = new("TestEntity", "key1");

        mockClient
            .Setup(c => c.SignalEntityAsync(id, "Delete", null, null, default))
            .Returns(Task.CompletedTask);

        ITestEntityProxy proxy = mockClient.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        proxy.Delete();

        // Assert
        mockClient.Verify(
            c => c.SignalEntityAsync(id, "Delete", null, null, default),
            Times.Once);
    }

    [Fact]
    public void CallMethod_WithTaskOfT_ThrowsNotSupportedException()
    {
        // Arrange
        Mock<DurableEntityClient> mockClient = new("test");
        EntityInstanceId id = new("TestEntity", "key1");

        ITestEntityProxy proxy = mockClient.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        Func<Task> act = async () => await proxy.GetValue();

        // Assert
        act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*returns Task<T>*not supported for client-side entity proxies*");
    }

    [Fact]
    public async Task CallMethod_WithMultipleParameters_PassesParametersAsArray()
    {
        // Arrange
        Mock<DurableEntityClient> mockClient = new("test");
        EntityInstanceId id = new("TestEntity", "key1");
        string param1 = "test";
        int param2 = 42;

        mockClient
            .Setup(c => c.SignalEntityAsync(
                id,
                "Combine",
                It.Is<object?[]>(arr => arr != null && arr.Length == 2 && (string)arr[0]! == param1 && (int)arr[1]! == param2),
                null,
                default))
            .Returns(Task.CompletedTask);

        ITestEntityProxy proxy = mockClient.Object.CreateProxy<ITestEntityProxy>(id);

        // Act
        await proxy.Combine(param1, param2);

        // Assert
        mockClient.Verify(
            c => c.SignalEntityAsync(
                id,
                "Combine",
                It.Is<object?[]>(arr => arr != null && arr.Length == 2),
                null,
                default),
            Times.Once);
    }

    [Fact]
    public void CreateProxy_WithEntityNameAndKey_CreatesProxyWithCorrectId()
    {
        // Arrange
        Mock<DurableEntityClient> mockClient = new("test");
        string entityName = "TestEntity";
        string entityKey = "key1";

        mockClient
            .Setup(c => c.SignalEntityAsync(
                It.Is<EntityInstanceId>(id => id.Name == entityName.ToLowerInvariant() && id.Key == entityKey),
                "Delete",
                null,
                null,
                default))
            .Returns(Task.CompletedTask);

        // Act
        ITestEntityProxy proxy = mockClient.Object.CreateProxy<ITestEntityProxy>(entityName, entityKey);
        proxy.Delete();

        // Assert
        mockClient.Verify(
            c => c.SignalEntityAsync(
                It.Is<EntityInstanceId>(id => id.Name == entityName.ToLowerInvariant() && id.Key == entityKey),
                "Delete",
                null,
                null,
                default),
            Times.Once);
    }

    public interface ITestEntityProxy : IEntityProxy
    {
        Task<int> GetValue();

        Task Add(int value);

        Task Reset();

        void Delete();

        Task Combine(string str, int num);
    }
}
