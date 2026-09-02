// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Security.Cryptography;
using DurableTask.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Worker.Shims;

public class TaskOrchestrationShimTests
{
    static readonly FieldInfo ShimWrapperContextField = typeof(TaskOrchestrationShim)
        .GetField("wrapperContext", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(TaskOrchestrationShim)}.wrapperContext was not found.");

    static readonly FieldInfo CachedHashAlgorithmField = typeof(TaskOrchestrationContextWrapper)
        .GetField("cachedHashAlgorithm", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"{nameof(TaskOrchestrationContextWrapper)}.cachedHashAlgorithm was not found.");

    [Fact]
    public void Dispose_ForwardsToWrapperContext_ReleasingCachedHashAlgorithm()
    {
        // Arrange. We inject the wrapper context the same way Execute() would, so we can prove that
        // TaskOrchestrationShim.Dispose() forwards to TaskOrchestrationContextWrapper.Dispose(),
        // actually releasing the cached SHA1 instance backing the deterministic NewGuid()
        // optimization from issue #778 (not merely dereferencing it).
        TaskOrchestrationShim shim = CreateShim();
        TaskOrchestrationContextWrapper wrapperContext = CreateWrapperContext();
        wrapperContext.NewGuid(); // Populate the cached SHA1 instance.
        ShimWrapperContextField.SetValue(shim, wrapperContext);

        SHA1 cachedInstance = (SHA1)(CachedHashAlgorithmField.GetValue(wrapperContext)
            ?? throw new InvalidOperationException("NewGuid() did not populate cachedHashAlgorithm."));

        // Act
        shim.Dispose();

        // Assert
        CachedHashAlgorithmField.GetValue(wrapperContext).Should().BeNull();
        Action useAfterDispose = () => cachedInstance.ComputeHash(new byte[] { 1, 2, 3 });
        useAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task PublicFactory_CreateOrchestration_DoesNotCacheHashAlgorithm()
    {
        // Arrange
        DurableTaskShimFactory factory = new();
        CapturingNewGuidOrchestrator orchestrator = new();
        TaskOrchestration shim = factory.CreateOrchestration("Test", orchestrator);
        TestOrchestrationContext innerContext = new();

        // Act
        await shim.Execute(innerContext, "null");

        // Assert
        orchestrator.AfterFirstCall.Should().BeNull();
        orchestrator.AfterSecondCall.Should().BeNull();
    }

    [Fact]
    public async Task InternalFactory_CreateOrchestration_ReusesHashAlgorithmUntilDisposed()
    {
        // Arrange
        DurableTaskShimFactory factory = new();
        CapturingNewGuidOrchestrator orchestrator = new();
        TaskOrchestration shim = factory.CreateOrchestrationWithManagedLifetime(
            "Test",
            orchestrator,
            parent: null);
        TestOrchestrationContext innerContext = new();

        // Act
        await shim.Execute(innerContext, "null");

        // Assert
        SHA1 cachedHashAlgorithm = orchestrator.AfterFirstCall.Should().BeAssignableTo<SHA1>().Which;
        orchestrator.AfterSecondCall.Should().BeSameAs(cachedHashAlgorithm);

        ((IDisposable)shim).Dispose();
        Action useAfterDispose = () => cachedHashAlgorithm.ComputeHash([1, 2, 3]);
        useAfterDispose.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_WithoutExecute_DoesNotThrow()
    {
        // Arrange. Execute() was never called, so the shim's wrapperContext field is still null. This
        // must remain a safe no-op (e.g. eviction/teardown paths may run before the shim ever executes).
        TaskOrchestrationShim shim = CreateShim();

        // Act
        Action dispose = shim.Dispose;

        // Assert
        dispose.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange. Both the processor's try/finally and, defensively, an eviction callback could end up
        // disposing the same shim; disposal must be idempotent.
        TaskOrchestrationShim shim = CreateShim();
        TaskOrchestrationContextWrapper wrapperContext = CreateWrapperContext();
        wrapperContext.NewGuid();
        ShimWrapperContextField.SetValue(shim, wrapperContext);

        // Act
        Action dispose = () =>
        {
            shim.Dispose();
            shim.Dispose();
        };

        // Assert
        dispose.Should().NotThrow();
    }

    static TaskOrchestrationShim CreateShim()
    {
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        return new TaskOrchestrationShim(invocationContext, new NoOpOrchestrator());
    }

    static TaskOrchestrationContextWrapper CreateWrapperContext()
    {
        OrchestrationInvocationContext invocationContext = new(
            "Test",
            new(),
            NullLoggerFactory.Instance,
            null,
            ReuseNewGuidHashAlgorithm: true);
        TestOrchestrationContext innerContext = new();
        return new TaskOrchestrationContextWrapper(innerContext, invocationContext, deserializedInput: null);
    }

    sealed class CapturingNewGuidOrchestrator : ITaskOrchestrator
    {
        public Type InputType => typeof(object);

        public Type OutputType => typeof(object);

        public object? AfterFirstCall { get; private set; }

        public object? AfterSecondCall { get; private set; }

        public Task<object?> RunAsync(TaskOrchestrationContext context, object? input)
        {
            context.NewGuid();
            this.AfterFirstCall = CachedHashAlgorithmField.GetValue(context);
            context.NewGuid();
            this.AfterSecondCall = CachedHashAlgorithmField.GetValue(context);
            return Task.FromResult(input);
        }
    }

    sealed class NoOpOrchestrator : ITaskOrchestrator
    {
        public Type InputType => typeof(object);

        public Type OutputType => typeof(object);

        public Task<object?> RunAsync(TaskOrchestrationContext context, object? input) => Task.FromResult(input);
    }

    sealed class TestOrchestrationContext : OrchestrationContext
    {
        public TestOrchestrationContext()
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            };
        }

        public override void ContinueAsNew(object input) => throw new NotImplementedException();

        public override void ContinueAsNew(string newVersion, object input) => throw new NotImplementedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input)
            => throw new NotImplementedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input)
            => throw new NotImplementedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input, IDictionary<string, string> tags)
            => throw new NotImplementedException();

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state) => throw new NotImplementedException();

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancelToken)
            => throw new NotImplementedException();

        public override Task<TResult> ScheduleTask<TResult>(string name, string version, params object[] parameters)
            => throw new NotImplementedException();

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
            => throw new NotImplementedException();
    }
}
