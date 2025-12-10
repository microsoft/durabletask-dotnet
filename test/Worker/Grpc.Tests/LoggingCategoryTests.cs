// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Tests for logging category functionality, including dual-category emission for backward compatibility.
/// </summary>
public class LoggingCategoryTests
{
    const string NewGrpcCategory = "Microsoft.DurableTask.Worker.Grpc";
    const string LegacyCategory = "Microsoft.DurableTask";

    [Fact]
    public void Worker_UsesLegacyCategories_ByDefault()
    {
        // Arrange & Act
        var workerOptions = new DurableTaskWorkerOptions();

        // Assert
        workerOptions.Logging.UseLegacyCategories.Should().BeTrue("backward compatibility should be enabled by default");
    }

    [Fact]
    public void Worker_CanDisableLegacyCategories()
    {
        // Arrange
        var workerOptions = new DurableTaskWorkerOptions
        {
            Logging = { UseLegacyCategories = false }
        };

        // Act & Assert
        workerOptions.Logging.UseLegacyCategories.Should().BeFalse("legacy categories can be explicitly disabled");
    }

    [Fact]
    public void DualCategoryLogger_LogsToBothLoggers_WhenBothEnabled()
    {
        // Arrange
        var primaryLogger = new Mock<ILogger>();
        var legacyLogger = new Mock<ILogger>();

        primaryLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        legacyLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var dualLogger = new DualCategoryLogger(primaryLogger.Object, legacyLogger.Object);

        // Act
        dualLogger.LogInformation("Test message");

        // Assert - verify both loggers received the log call
        primaryLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Test message")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "primary logger should receive the log");

        legacyLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Test message")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "legacy logger should receive the log");
    }

    [Fact]
    public void DualCategoryLogger_LogsToPrimaryOnly_WhenLegacyIsNull()
    {
        // Arrange
        var logProvider = new TestLogProvider(new NullOutput());
        var loggerFactory = new SimpleLoggerFactory(logProvider);

        ILogger primaryLogger = loggerFactory.CreateLogger(NewGrpcCategory);

        var dualLogger = new DualCategoryLogger(primaryLogger, null);

        // Act
        dualLogger.LogInformation("Test message");

        // Assert
        logProvider.TryGetLogs(NewGrpcCategory, out var newLogs).Should().BeTrue();
        newLogs.Should().ContainSingle(l => l.Message.Contains("Test message"));
    }

    [Fact]
    public void DualCategoryLogger_IsEnabled_ReturnsTrueIfEitherLoggerEnabled()
    {
        // Arrange
        var primaryLogger = new Mock<ILogger>();
        var legacyLogger = new Mock<ILogger>();

        primaryLogger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);
        legacyLogger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(false);

        var dualLogger = new DualCategoryLogger(primaryLogger.Object, legacyLogger.Object);

        // Act
        bool result = dualLogger.IsEnabled(LogLevel.Information);

        // Assert
        result.Should().BeTrue("at least one logger is enabled");
    }

    [Fact]
    public void DualCategoryLogger_IsEnabled_ReturnsFalseIfNeitherLoggerEnabled()
    {
        // Arrange
        var primaryLogger = new Mock<ILogger>();
        var legacyLogger = new Mock<ILogger>();

        primaryLogger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(false);
        legacyLogger.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(false);

        var dualLogger = new DualCategoryLogger(primaryLogger.Object, legacyLogger.Object);

        // Act
        bool result = dualLogger.IsEnabled(LogLevel.Information);

        // Assert
        result.Should().BeFalse("neither logger is enabled");
    }

    [Fact]
    public void DualCategoryLogger_BeginScope_CreatesScopeInBothLoggers()
    {
        // Arrange
        var primaryLogger = new Mock<ILogger>();
        var legacyLogger = new Mock<ILogger>();

        var primaryDisposable = new Mock<IDisposable>();
        var legacyDisposable = new Mock<IDisposable>();

        primaryLogger.Setup(l => l.BeginScope(It.IsAny<string>())).Returns(primaryDisposable.Object);
        legacyLogger.Setup(l => l.BeginScope(It.IsAny<string>())).Returns(legacyDisposable.Object);

        var dualLogger = new DualCategoryLogger(primaryLogger.Object, legacyLogger.Object);

        // Act
        using IDisposable? scope = dualLogger.BeginScope("test");

        // Assert
        primaryLogger.Verify(l => l.BeginScope("test"), Times.Once);
        legacyLogger.Verify(l => l.BeginScope("test"), Times.Once);

        scope.Should().NotBeNull();
    }

    [Fact]
    public void LoggingOptions_UseLegacyCategories_DefaultsToTrue()
    {
        // Arrange & Act
        var options = new DurableTaskWorkerOptions();

        // Assert
        options.Logging.UseLegacyCategories.Should().BeTrue("backward compatibility is enabled by default");
    }
}

sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
{
    readonly T value;

    public OptionsMonitorStub(T value)
    {
        this.value = value;
    }

    public T CurrentValue => this.value;

    public T Get(string? name) => this.value;

    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

sealed class NullOutput : ITestOutputHelper
{
    public void WriteLine(string message) { }
    public void WriteLine(string format, params object[] args) { }
}

sealed class SimpleLoggerFactory : ILoggerFactory
{
    readonly ILoggerProvider provider;

    public SimpleLoggerFactory(ILoggerProvider provider)
    {
        this.provider = provider;
    }

    public void AddProvider(ILoggerProvider provider)
    {
        // No-op; single provider
    }

    public ILogger CreateLogger(string categoryName) => this.provider.CreateLogger(categoryName);

    public void Dispose() { }
}
