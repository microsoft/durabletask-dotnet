// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class PayloadInterceptorTests
{
    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        value => Encoding.UTF8.GetBytes(value),
        bytes => Encoding.UTF8.GetString(bytes));
    static readonly Method<string, string> TestMethod = new(
        MethodType.Unary,
        "TestService",
        "TestMethod",
        StringMarshaller,
        StringMarshaller);

    [Fact]
    public void AsyncUnaryCall_SynchronousExternalization_RunsInlineWithoutThreadHop()
    {
        // Arrange: externalization completes synchronously (no real async work), so there is no
        // await point for the state machine to yield on. Capture the managed thread ID that both
        // callbacks observe and compare it to the calling thread's ID. This is deterministic
        // (unlike checking "was it invoked yet", which races against thread-pool scheduling): the
        // calling thread is busy executing this synchronous test method the whole time, so it is
        // physically impossible for a Task.Run-dispatched worker item to run on this same managed
        // thread while we're still inside the call. If the Task.Run thread-pool hop were still
        // present, the observed thread ID would either be null (not yet run) or a different thread.
        int callingThreadId = Environment.CurrentManagedThreadId;
        int? externalizeThreadId = null;
        int? continuationThreadId = null;
        TestPayloadInterceptor interceptor = CreateInterceptor(
            onExternalize: (_, _) =>
            {
                externalizeThreadId = Environment.CurrentManagedThreadId;
                return Task.CompletedTask;
            });
        ClientInterceptorContext<string, string> context = CreateContext();

        SynchronizationContext? previous = SynchronizationContext.Current;
        AsyncUnaryCall<string> call;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);

            // Act
            call = interceptor.AsyncUnaryCall(
                "request",
                context,
                (req, ctx) =>
                {
                    continuationThreadId = Environment.CurrentManagedThreadId;
                    return CreateFakeCall("response");
                });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // Assert: both callbacks already ran, synchronously, on the calling thread.
        externalizeThreadId.Should().Be(callingThreadId);
        continuationThreadId.Should().Be(callingThreadId);
        call.Dispose();
    }

    [Fact]
    public async Task AsyncUnaryCall_Success_ExternalizesThenInvokesContinuationThenResolves()
    {
        // Arrange
        List<string> order = [];
        bool disposeInvoked = false;
        TestPayloadInterceptor interceptor = CreateInterceptor(
            onExternalize: (_, _) =>
            {
                order.Add("externalize");
                return Task.CompletedTask;
            },
            onResolve: (_, _) =>
            {
                order.Add("resolve");
                return Task.CompletedTask;
            });
        ClientInterceptorContext<string, string> context = CreateContext();

        // Act: no using declaration here — Dispose() is called explicitly exactly once below so
        // the disposal assertion isn't muddied by a second, implicit Dispose() at scope exit.
        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            context,
            (req, ctx) =>
            {
                order.Add("continuation");
                return CreateFakeCall("response", disposeAction: () => disposeInvoked = true);
            });
        string response = await call.ResponseAsync;
        Metadata headers = await call.ResponseHeadersAsync;

        // Assert: ordering, response payload, and pass-through metadata are all preserved.
        order.Should().Equal("externalize", "continuation", "resolve");
        response.Should().Be("response");
        headers.Should().NotBeNull();
        call.GetStatus().StatusCode.Should().Be(StatusCode.OK);
        call.GetTrailers().Should().NotBeNull();

        call.Dispose();
        disposeInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task AsyncUnaryCall_ExternalizeFails_PropagatesFailureAndSkipsContinuation()
    {
        // Arrange
        InvalidOperationException failure = new("externalization failed");
        bool continuationInvoked = false;
        TestPayloadInterceptor interceptor = CreateInterceptor(
            onExternalize: (_, _) => throw failure);
        ClientInterceptorContext<string, string> context = CreateContext();

        // Act
        using AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            context,
            (req, ctx) =>
            {
                continuationInvoked = true;
                return CreateFakeCall("response");
            });
        Func<Task> act = async () => await call.ResponseAsync;

        // Assert: the gRPC call is never sent, and the original exception surfaces to the caller.
        continuationInvoked.Should().BeFalse();
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().Be(failure);
        call.GetStatus().StatusCode.Should().Be(StatusCode.Internal);
    }

    [Fact]
    public async Task AsyncUnaryCall_CancellationRequested_PropagatesCancellationAndSkipsContinuation()
    {
        // Arrange
        using CancellationTokenSource cts = new();
        cts.Cancel();
        bool continuationInvoked = false;
        TestPayloadInterceptor interceptor = CreateInterceptor(
            onExternalize: (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        ClientInterceptorContext<string, string> context = CreateContext(cts.Token);

        // Act
        using AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            context,
            (req, ctx) =>
            {
                continuationInvoked = true;
                return CreateFakeCall("response");
            });
        Func<Task> act = async () => await call.ResponseAsync;

        // Assert
        continuationInvoked.Should().BeFalse();
        await act.Should().ThrowAsync<OperationCanceledException>();
        call.GetStatus().StatusCode.Should().Be(StatusCode.Cancelled);
    }

    [Fact]
    public async Task AsyncUnaryCall_DisposeBeforeCompletion_DisposesInnerCallOnceStarted()
    {
        // Arrange: hold externalization open so Dispose() races ahead of call construction.
        TaskCompletionSource externalizeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposeInvoked = false;

        // Dispose() schedules its inner-call disposal via a ContinueWith on startCallTask, which is
        // a separate continuation from the one driving ResponseAsync(); the two are not ordered
        // relative to each other. Signal a TCS from the disposal callback and await it (with a
        // bounded timeout) instead of asserting immediately after ResponseAsync completes, so the
        // test doesn't race against Dispose's continuation.
        TaskCompletionSource disposeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestPayloadInterceptor interceptor = CreateInterceptor(
            onExternalize: async (_, _) => await externalizeGate.Task);
        ClientInterceptorContext<string, string> context = CreateContext();

        // Act
        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            context,
            (req, ctx) => CreateFakeCall(
                "response",
                disposeAction: () =>
                {
                    disposeInvoked = true;
                    disposeSignal.TrySetResult();
                }));
        call.Dispose();

        // Assert: not disposed yet because the inner call has not been created.
        disposeInvoked.Should().BeFalse();

        externalizeGate.SetResult();
        await call.ResponseAsync;

        Task completed = await Task.WhenAny(disposeSignal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(disposeSignal.Task, "the inner call's dispose action should be invoked once it exists");

        // Assert: once the inner call exists, disposal is applied to it.
        disposeInvoked.Should().BeTrue();
    }

    [Fact]
    public void AsyncUnaryCall_DisposeAfterExternalizeFailure_DoesNotThrowOrDisposeMissingCall()
    {
        // Arrange
        bool disposeInvoked = false;
        TestPayloadInterceptor interceptor = CreateInterceptor(
            onExternalize: (_, _) => throw new InvalidOperationException("boom"));
        ClientInterceptorContext<string, string> context = CreateContext();

        // Act
        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            context,
            (req, ctx) => CreateFakeCall("response", disposeAction: () => disposeInvoked = true));
        Action act = call.Dispose;

        // Assert: no inner call was ever created, so Dispose is a safe no-op.
        act.Should().NotThrow();
        disposeInvoked.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncUnaryCall_IncompleteInterceptorAwait_DoesNotRequireCallerContextPump(bool resolve)
    {
        // Arrange
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        QueuedSynchronizationContext blockedContext = new();
        Func<object?, CancellationToken, Task> asynchronousStep = async (_, _) =>
        {
            entered.SetResult();
            await gate.Task;
        };
        TestPayloadInterceptor interceptor = resolve
            ? CreateInterceptor(onResolve: asynchronousStep)
            : CreateInterceptor(onExternalize: asynchronousStep);
        ClientInterceptorContext<string, string> context = CreateContext();

        SynchronizationContext? previous = SynchronizationContext.Current;
        AsyncUnaryCall<string> call;
        try
        {
            SynchronizationContext.SetSynchronizationContext(blockedContext);

            // Act
            call = interceptor.AsyncUnaryCall(
                "request",
                context,
                (_, _) => CreateFakeCall("response"));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.SetResult();

            Task winner = await Task.WhenAny(call.ResponseAsync, blockedContext.CallbackPosted)
                .WaitAsync(TimeSpan.FromSeconds(5));

            // Assert
            winner.Should().Be(call.ResponseAsync, "the response must complete without pumping the caller's context");
            await call.ResponseAsync;
        }
        finally
        {
            gate.TrySetResult();
            blockedContext.Drain();
            call.Dispose();
        }
    }

    static TestPayloadInterceptor CreateInterceptor(
        Func<object?, CancellationToken, Task>? onExternalize = null,
        Func<object?, CancellationToken, Task>? onResolve = null)
    {
        Mock<PayloadStore> payloadStore = new();
        return new TestPayloadInterceptor(payloadStore.Object, new LargePayloadStorageOptions())
        {
            OnExternalize = onExternalize,
            OnResolve = onResolve,
        };
    }

    static ClientInterceptorContext<string, string> CreateContext(CancellationToken cancellationToken = default)
    {
        CallOptions options = new(cancellationToken: cancellationToken);
        return new ClientInterceptorContext<string, string>(TestMethod, "localhost", options);
    }

    static AsyncUnaryCall<string> CreateFakeCall(string response, Action? disposeAction = null)
    {
        return new AsyncUnaryCall<string>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, string.Empty),
            () => new Metadata(),
            disposeAction ?? (() => { }));
    }

    sealed class TestPayloadInterceptor(PayloadStore payloadStore, LargePayloadStorageOptions options)
        : PayloadInterceptor<object, object>(payloadStore, options)
    {
        public Func<object?, CancellationToken, Task>? OnExternalize { get; set; }

        public Func<object?, CancellationToken, Task>? OnResolve { get; set; }

        protected override Task ExternalizeRequestPayloadsAsync<TRequest>(TRequest request, CancellationToken cancellation)
            => this.OnExternalize?.Invoke(request, cancellation) ?? Task.CompletedTask;

        protected override Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation)
            => this.OnResolve?.Invoke(response, cancellation) ?? Task.CompletedTask;
    }

    sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = [];
        readonly TaskCompletionSource callbackPosted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CallbackPosted => this.callbackPosted.Task;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (this.callbacks)
            {
                this.callbacks.Enqueue((callback, state));
            }

            this.callbackPosted.TrySetResult();
        }

        public void Drain()
        {
            SynchronizationContext? previous = Current;
            SetSynchronizationContext(this);
            try
            {
                while (true)
                {
                    (SendOrPostCallback Callback, object? State) work;
                    lock (this.callbacks)
                    {
                        if (this.callbacks.Count == 0)
                        {
                            return;
                        }

                        work = this.callbacks.Dequeue();
                    }

                    work.Callback(work.State);
                }
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}
