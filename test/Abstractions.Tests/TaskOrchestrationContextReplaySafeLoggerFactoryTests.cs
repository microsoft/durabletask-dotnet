// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Tests;

public class TaskOrchestrationContextReplaySafeLoggerFactoryTests
{
    [Fact]
    public void ReplaySafeLoggerFactory_ReturnsCachedFactoryInstance()
    {
        // Arrange
        TrackingLoggerProvider provider = new();
        TrackingLoggerFactory loggerFactory = new(provider);
        TestTaskOrchestrationContext context = new(loggerFactory, isReplaying: false);

        // Act
        ILoggerFactory firstFactory = context.ReplaySafeLoggerFactory;
        ILoggerFactory secondFactory = context.ReplaySafeLoggerFactory;

        // Assert
        secondFactory.Should().BeSameAs(firstFactory);
    }

    [Fact]
    public void ReplaySafeLoggerFactory_CreateLogger_SuppressesLogsDuringReplay()
    {
        // Arrange
        TrackingLoggerProvider provider = new();
        TrackingLoggerFactory loggerFactory = new(provider);
        TestTaskOrchestrationContext context = new(loggerFactory, isReplaying: true);
        ILogger logger = context.ReplaySafeLoggerFactory.CreateLogger("ReplaySafe");

        // Act
        logger.LogInformation("This log should be suppressed.");

        // Assert
        provider.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ReplaySafeLoggerFactory_CreateLogger_WritesLogsWhenNotReplaying()
    {
        // Arrange
        TrackingLoggerProvider provider = new();
        TrackingLoggerFactory loggerFactory = new(provider);
        TestTaskOrchestrationContext context = new(loggerFactory, isReplaying: false);
        ILogger logger = context.ReplaySafeLoggerFactory.CreateLogger("ReplaySafe");

        // Act
        logger.LogInformation("This log should be written.");

        // Assert
        provider.Entries.Should().ContainSingle(entry =>
            entry.CategoryName == "ReplaySafe" &&
            entry.Message.Contains("This log should be written.", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaySafeLoggerFactory_AddProvider_ThrowsWithoutMutatingUnderlyingFactory()
    {
        // Arrange
        TrackingLoggerProvider provider = new();
        TrackingLoggerFactory loggerFactory = new(provider);
        TestTaskOrchestrationContext context = new(loggerFactory, isReplaying: false);
        TrackingLoggerProvider additionalProvider = new();

        // Act
        Action act = () => context.ReplaySafeLoggerFactory.AddProvider(additionalProvider);

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*replay-safe logger factory*not supported*");
        loggerFactory.AddProviderCallCount.Should().Be(0);
    }

    [Fact]
    public void ReplaySafeLoggerFactory_CreateLogger_FromWrappedContext_ChecksReplayOnce()
    {
        // Arrange
        TrackingLoggerProvider provider = new();
        TrackingLoggerFactory loggerFactory = new(provider);
        TestTaskOrchestrationContext innerContext = new(loggerFactory, isReplaying: false);
        WrappingTaskOrchestrationContext wrappedContext = new(innerContext);
        ILogger logger = wrappedContext.ReplaySafeLoggerFactory.CreateLogger("ReplaySafe");

        // Act
        logger.LogInformation("This log should be written.");

        // Assert
        innerContext.IsReplayingAccessCount.Should().Be(1);
        provider.Entries.Should().ContainSingle(entry =>
            entry.CategoryName == "ReplaySafe" &&
            entry.Message.Contains("This log should be written.", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaySafeLoggerFactory_CreateLogger_ThrowsOnCyclicLoggerFactory()
    {
        // Arrange
        SelfReferencingContext cyclicContext = new();

        // Act
        Action act = () => cyclicContext.ReplaySafeLoggerFactory.CreateLogger("Test");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maximum unwrap depth exceeded*");
    }

    [Fact]
    public void ReplaySafeLoggerFactory_Dispose_DoesNotDisposeUnderlyingFactory()
    {
        // Arrange
        TrackingLoggerProvider provider = new();
        TrackingLoggerFactory loggerFactory = new(provider);
        TestTaskOrchestrationContext context = new(loggerFactory, isReplaying: false);

        // Act
        context.ReplaySafeLoggerFactory.Dispose();

        // Assert
        loggerFactory.DisposeCallCount.Should().Be(0);
    }

    sealed class TestTaskOrchestrationContext : TaskOrchestrationContext
    {
        readonly ILoggerFactory loggerFactory;
        readonly bool isReplaying;

        public TestTaskOrchestrationContext(ILoggerFactory loggerFactory, bool isReplaying)
        {
            this.loggerFactory = loggerFactory;
            this.isReplaying = isReplaying;
        }

        public override TaskName Name => default;

        public override string InstanceId => "test-instance";

        public override ParentOrchestrationInstance? Parent => null;

        public override DateTime CurrentUtcDateTime => DateTime.UnixEpoch;

        public int IsReplayingAccessCount { get; private set; }

        public override bool IsReplaying
        {
            get
            {
                this.IsReplayingAccessCount++;
                return this.isReplaying;
            }
        }

        public override IReadOnlyDictionary<string, object?> Properties => new Dictionary<string, object?>();

        protected override ILoggerFactory LoggerFactory => this.loggerFactory;

        public override T GetInput<T>()
            => default!;

        public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
            => throw new NotImplementedException();

        public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override void SendEvent(string instanceId, string eventName, object payload)
            => throw new NotImplementedException();

        public override void SetCustomStatus(object? customStatus)
            => throw new NotImplementedException();

        public override Task<TResult> CallSubOrchestratorAsync<TResult>(
            TaskName orchestratorName,
            object? input = null,
            TaskOptions? options = null)
            => throw new NotImplementedException();

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
            => throw new NotImplementedException();

        public override Guid NewGuid()
            => throw new NotImplementedException();
    }

    sealed class WrappingTaskOrchestrationContext : TaskOrchestrationContext
    {
        readonly TaskOrchestrationContext innerContext;

        public WrappingTaskOrchestrationContext(TaskOrchestrationContext innerContext)
        {
            this.innerContext = innerContext ?? throw new ArgumentNullException(nameof(innerContext));
        }

        public override TaskName Name => this.innerContext.Name;

        public override string InstanceId => this.innerContext.InstanceId;

        public override ParentOrchestrationInstance? Parent => this.innerContext.Parent;

        public override DateTime CurrentUtcDateTime => this.innerContext.CurrentUtcDateTime;

        public override bool IsReplaying => this.innerContext.IsReplaying;

        public override string Version => this.innerContext.Version;

        public override IReadOnlyDictionary<string, object?> Properties => this.innerContext.Properties;

        protected override ILoggerFactory LoggerFactory => this.innerContext.ReplaySafeLoggerFactory;

        public override T GetInput<T>()
            => this.innerContext.GetInput<T>()!;

        public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
            => this.innerContext.CallActivityAsync<TResult>(name, input, options);

        public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
            => this.innerContext.CreateTimer(fireAt, cancellationToken);

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
            => this.innerContext.WaitForExternalEvent<T>(eventName, cancellationToken);

        public override void SendEvent(string instanceId, string eventName, object payload)
            => this.innerContext.SendEvent(instanceId, eventName, payload);

        public override void SetCustomStatus(object? customStatus)
            => this.innerContext.SetCustomStatus(customStatus);

        public override Task<TResult> CallSubOrchestratorAsync<TResult>(
            TaskName orchestratorName,
            object? input = null,
            TaskOptions? options = null)
            => this.innerContext.CallSubOrchestratorAsync<TResult>(orchestratorName, input, options);

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
            => this.innerContext.ContinueAsNew(newInput, preserveUnprocessedEvents);

        public override Guid NewGuid()
            => this.innerContext.NewGuid();
    }

    sealed class SelfReferencingContext : TaskOrchestrationContext
    {
        public SelfReferencingContext()
        {
        }

        public override TaskName Name => default;

        public override string InstanceId => "cyclic-instance";

        public override ParentOrchestrationInstance? Parent => null;

        public override DateTime CurrentUtcDateTime => DateTime.UnixEpoch;

        public override bool IsReplaying => false;

        public override IReadOnlyDictionary<string, object?> Properties => new Dictionary<string, object?>();

        // Bug: points at self instead of an inner context — should cause cycle detection.
        protected override ILoggerFactory LoggerFactory => this.ReplaySafeLoggerFactory;

        public override T GetInput<T>()
            => default!;

        public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
            => throw new NotImplementedException();

        public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public override void SendEvent(string instanceId, string eventName, object payload)
            => throw new NotImplementedException();

        public override void SetCustomStatus(object? customStatus)
            => throw new NotImplementedException();

        public override Task<TResult> CallSubOrchestratorAsync<TResult>(
            TaskName orchestratorName,
            object? input = null,
            TaskOptions? options = null)
            => throw new NotImplementedException();

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
            => throw new NotImplementedException();

        public override Guid NewGuid()
            => throw new NotImplementedException();
    }

    sealed class TrackingLoggerFactory : ILoggerFactory
    {
        readonly TrackingLoggerProvider provider;

        public TrackingLoggerFactory(TrackingLoggerProvider provider)
        {
            this.provider = provider;
        }

        public int AddProviderCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public void AddProvider(ILoggerProvider provider)
        {
            this.AddProviderCallCount++;
        }

        public ILogger CreateLogger(string categoryName)
            => this.provider.CreateLogger(categoryName);

        public void Dispose()
        {
            this.DisposeCallCount++;
        }
    }

    sealed class TrackingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
            => new TrackingLogger(categoryName, this.Entries);

        public void Dispose()
        {
        }
    }

    sealed class TrackingLogger : ILogger
    {
        readonly string categoryName;
        readonly List<LogEntry> entries;

        public TrackingLogger(string categoryName, List<LogEntry> entries)
        {
            this.categoryName = categoryName;
            this.entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            this.entries.Add(new LogEntry(this.categoryName, logLevel, formatter(state, exception)));
        }
    }

    sealed record LogEntry(string CategoryName, LogLevel LogLevel, string Message);

    sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
