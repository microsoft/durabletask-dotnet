// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class AuthorizationPluginTests
{
    [Fact]
    public async Task AuthorizationPlugin_AllowsAuthorizedOrchestration()
    {
        // Arrange
        Mock<IAuthorizationHandler> handler = new();
        handler.Setup(h => h.AuthorizeAsync(It.IsAny<AuthorizationContext>()))
            .ReturnsAsync(true);

        AuthorizationPlugin plugin = new(handler.Object);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act & Assert
        await plugin.OrchestrationInterceptors[0].Invoking(
            i => i.OnOrchestrationStartingAsync(context))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task AuthorizationPlugin_DeniesUnauthorizedOrchestration()
    {
        // Arrange
        Mock<IAuthorizationHandler> handler = new();
        handler.Setup(h => h.AuthorizeAsync(It.IsAny<AuthorizationContext>()))
            .ReturnsAsync(false);

        AuthorizationPlugin plugin = new(handler.Object);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, null);

        // Act & Assert
        await plugin.OrchestrationInterceptors[0].Invoking(
            i => i.OnOrchestrationStartingAsync(context))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*TestOrch*");
    }

    [Fact]
    public async Task AuthorizationPlugin_DeniesUnauthorizedActivity()
    {
        // Arrange
        Mock<IAuthorizationHandler> handler = new();
        handler.Setup(h => h.AuthorizeAsync(It.IsAny<AuthorizationContext>()))
            .ReturnsAsync(false);

        AuthorizationPlugin plugin = new(handler.Object);
        ActivityInterceptorContext context = new("TestActivity", "instance1", "input");

        // Act & Assert
        await plugin.ActivityInterceptors[0].Invoking(
            i => i.OnActivityStartingAsync(context))
            .Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*TestActivity*");
    }

    [Fact]
    public async Task AuthorizationPlugin_PassesCorrectContextToHandler()
    {
        // Arrange
        AuthorizationContext? capturedContext = null;
        Mock<IAuthorizationHandler> handler = new();
        handler.Setup(h => h.AuthorizeAsync(It.IsAny<AuthorizationContext>()))
            .Callback<AuthorizationContext>(c => capturedContext = c)
            .ReturnsAsync(true);

        AuthorizationPlugin plugin = new(handler.Object);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, "some-input");

        // Act
        await plugin.OrchestrationInterceptors[0].OnOrchestrationStartingAsync(context);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.Name.Should().Be(new TaskName("TestOrch"));
        capturedContext.InstanceId.Should().Be("instance1");
        capturedContext.TargetType.Should().Be(AuthorizationTargetType.Orchestration);
        capturedContext.Input.Should().Be("some-input");
    }
}
