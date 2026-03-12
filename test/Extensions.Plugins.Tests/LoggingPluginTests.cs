// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class LoggingPluginTests
{
    [Fact]
    public void LoggingPlugin_HasCorrectName()
    {
        // Arrange
        Mock<ILoggerFactory> loggerFactory = new();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());

        // Act
        LoggingPlugin plugin = new(loggerFactory.Object);

        // Assert
        plugin.Name.Should().Be(LoggingPlugin.DefaultName);
    }

    [Fact]
    public void LoggingPlugin_HasOneOrchestrationInterceptor()
    {
        // Arrange
        Mock<ILoggerFactory> loggerFactory = new();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());

        // Act
        LoggingPlugin plugin = new(loggerFactory.Object);

        // Assert
        plugin.OrchestrationInterceptors.Should().HaveCount(1);
        plugin.ActivityInterceptors.Should().HaveCount(1);
    }
}
